using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Abstractions.Terraform;

/// <summary>
/// Runs an external process safely: <c>UseShellExecute=false</c>, redirected stdout/stderr, an explicit
/// working directory, arguments passed via <c>ArgumentList</c> (never a shell string), process-scoped
/// environment variables, cancellation, and process-tree termination. Output is streamed line-by-line
/// through <paramref name="output"/>. See docs/05-terraform-engine.md.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Starts the process, streams each stdout/stderr line via <paramref name="output"/>, and completes
    /// when the process exits. Cancelling <paramref name="ct"/> kills the whole process tree and returns
    /// a result with <see cref="Contracts.Terraform.ProcessResult.Cancelled"/> set.
    /// </summary>
    Task<ProcessResult> RunAsync(
        TerraformCommandRequest request,
        IProgress<ProcessOutputEvent>? output,
        CancellationToken ct = default);
}
