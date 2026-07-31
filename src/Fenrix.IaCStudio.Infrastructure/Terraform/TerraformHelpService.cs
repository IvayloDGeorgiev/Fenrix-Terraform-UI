using System.Text;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Terraform;

/// <summary>
/// Runs <c>terraform -help</c> / <c>terraform &lt;cmd&gt; -help</c> through the normal executor spine and parses
/// the output for the dynamic command builder (Phase 12). Reuses <see cref="ITerraformExecutor"/> so the binary,
/// working directory, and version constraint are resolved exactly as for any other command, and the run flows
/// through the single ArgumentList spine. Help output carries no secrets, so it is captured and parsed in
/// memory (the executor still writes its usual redacted history + log). See docs/05-terraform-engine.md.
/// </summary>
public sealed class TerraformHelpService(
    ITerraformExecutor executor,
    ILogger<TerraformHelpService> logger) : ITerraformHelpService
{
    private readonly ITerraformExecutor _executor = executor;
    private readonly ILogger<TerraformHelpService> _logger = logger;

    public async Task<CommandCatalogResult> GetCommandsAsync(
        Guid projectId, Guid environmentId, CancellationToken ct = default)
    {
        var spec = new TerraformRunSpec(projectId, environmentId, TerraformCommandKind.Custom)
        {
            CustomArguments = ["-help"]
        };

        var plan = await _executor.PlanAsync(spec, ct);
        if (!plan.CanRun)
            return new CommandCatalogResult([], plan.BlockReason ?? "Terraform is unavailable.", null);

        var version = plan.Installation?.Version?.ToString();

        try
        {
            var text = await CaptureAsync(plan, ct);
            var commands = TerraformHelpParser.ParseCommandList(text);
            if (commands.Count == 0)
                return new CommandCatalogResult([], "Could not parse the Terraform command list.", version);
            return new CommandCatalogResult(commands, null, version);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list Terraform commands.");
            return new CommandCatalogResult([], ex.Message, version);
        }
    }

    public async Task<TerraformCommandHelp?> GetCommandHelpAsync(
        Guid projectId, Guid environmentId, string command, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(command))
            return null;

        var spec = new TerraformRunSpec(projectId, environmentId, TerraformCommandKind.Custom)
        {
            // "<cmd> -help" is always read-only (the classifier's help short-circuit lets even mutating
            // commands' help through), so we can show documentation for every command.
            CustomArguments = [command, "-help"]
        };

        var plan = await _executor.PlanAsync(spec, ct);
        if (!plan.CanRun)
            return null;

        try
        {
            var text = await CaptureAsync(plan, ct);
            return TerraformHelpParser.ParseCommandHelp(command, text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read help for terraform {Command}.", command);
            return null;
        }
    }

    /// <summary>Runs a resolved plan and returns its full captured output (stdout + stderr, in order).</summary>
    private async Task<string> CaptureAsync(TerraformRunPlan plan, CancellationToken ct)
    {
        var sink = new TextSink();
        await _executor.ExecuteAsync(plan, sink, ct);
        return sink.ToString();
    }

    /// <summary>Accumulates every output line so the help text can be parsed in memory.</summary>
    private sealed class TextSink : IProgress<ProcessOutputEvent>
    {
        private readonly StringBuilder _sb = new();
        public void Report(ProcessOutputEvent value)
        {
            lock (_sb) _sb.AppendLine(value.Text);
        }
        public override string ToString()
        {
            lock (_sb) return _sb.ToString();
        }
    }
}
