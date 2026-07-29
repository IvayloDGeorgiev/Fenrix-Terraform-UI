using Fenrix.IaCStudio.Application.Abstractions.Editor;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Editor;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Formats an editor buffer via <c>terraform fmt -</c> (stdin → stdout). Reuses <see cref="ITerraformExecutor"/>
/// to resolve the binary, working directory, and version constraint and to build the exact request + preview
/// (so preview == execution), then runs it through <see cref="TerraformProcessCoordinator"/> with
/// <c>captureLog:false</c> so the buffer — which may contain hardcoded secrets — is never written to a log.
/// See docs/05-terraform-engine.md, docs/11-secrets.md, docs/13-ui-design.md.
/// </summary>
public sealed class EditorFormatService(
    ITerraformExecutor executor,
    TerraformProcessCoordinator coordinator) : IEditorFormatService
{
    private readonly ITerraformExecutor _executor = executor;
    private readonly TerraformProcessCoordinator _coordinator = coordinator;

    public async Task<EditorFormatPreview> PreviewAsync(Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var spec = new TerraformRunSpec(projectId, environmentId, TerraformCommandKind.FormatStdin) { StandardInput = "" };
        var plan = await _executor.PlanAsync(spec, ct);
        return new EditorFormatPreview(plan.Preview, plan.BlockReason);
    }

    public async Task<EditorFormatResult> FormatAsync(
        Guid projectId, Guid environmentId, string buffer,
        IProgress<ProcessOutputEvent>? output = null, CancellationToken ct = default)
    {
        var spec = new TerraformRunSpec(projectId, environmentId, TerraformCommandKind.FormatStdin)
        {
            StandardInput = buffer
        };

        var plan = await _executor.PlanAsync(spec, ct);
        if (!plan.CanRun)
            return EditorFormatResult.Blocked(plan.BlockReason ?? "This command cannot be run.");

        try
        {
            // captureLog:false — fmt echoes the (potentially secret-bearing) buffer on stdout; keep it out of logs.
            var run = await _coordinator.RunAsync(plan.Request, output, false, ct);

            if (run.Process.Cancelled)
                return EditorFormatResult.Failed("Formatting was cancelled.");

            if (run.Process.ExitCode != 0)
            {
                // fmt writes parse errors to stderr; surface them so the user can fix the buffer.
                var message = run.FullOutput.Trim();
                return EditorFormatResult.Failed(string.IsNullOrWhiteSpace(message) ? "terraform fmt failed." : message);
            }

            // Terraform emits LF-terminated lines; the runner splits on newlines and the coordinator rejoins
            // them with the platform newline. Normalise to LF (fmt's canonical output) before comparing/returning.
            var formatted = NormalizeNewlines(run.StandardOutput);
            var changed = !string.Equals(formatted, NormalizeNewlines(buffer), StringComparison.Ordinal);
            return new EditorFormatResult(true, formatted, changed, null, null);
        }
        catch (Exception ex)
        {
            return EditorFormatResult.Failed(ex.Message);
        }
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace("\r", "\n");
}
