using System.Text;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Infrastructure.Checks;

/// <summary>
/// Runs an external check tool (TFLint / tfsec / Trivy / Infracost) through the shared safe process runner and
/// captures its stdout + stderr in memory for the caller to parse. Arguments always go through
/// <c>ArgumentList</c> (never a shell string). Output is <b>never written to a log file</b> — check output can
/// echo configuration values, so it stays in memory and only normalised findings/costs are surfaced. This is the
/// Checks equivalent of the Terraform process coordinator, minus history/logging. See docs/34-checks.md.
/// </summary>
public sealed class CheckProcessRunner(IProcessRunner runner)
{
    private readonly IProcessRunner _runner = runner;

    /// <summary>The captured outcome of a check-tool run.</summary>
    public sealed record CheckExecution(ProcessResult Process, string StandardOutput, string StandardError);

    public async Task<CheckExecution> ExecuteAsync(
        string executablePath,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        string label,
        CancellationToken ct = default)
    {
        var request = new ProcessStartRequest(executablePath, workingDirectory, arguments, environment, label);

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var capturing = new CapturingProgress(stdout, stderr);

        var result = await _runner.RunAsync(request, capturing, ct).ConfigureAwait(false);
        return new CheckExecution(result, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Synchronous capturing progress (mirrors <c>TerraformProcessCoordinator.CapturingProgress</c>): the
    /// process runner reports on threadpool threads, so appending synchronously under a lock avoids the
    /// <see cref="Progress{T}"/> sync-context race where the buffer may be read before posts are dispatched.
    /// </summary>
    private sealed class CapturingProgress(StringBuilder stdout, StringBuilder stderr) : IProgress<ProcessOutputEvent>
    {
        public void Report(ProcessOutputEvent value)
        {
            lock (stdout)
            {
                if (value.Stream == OutputStream.Stdout) stdout.AppendLine(value.Text);
                else stderr.AppendLine(value.Text);
            }
        }
    }
}
