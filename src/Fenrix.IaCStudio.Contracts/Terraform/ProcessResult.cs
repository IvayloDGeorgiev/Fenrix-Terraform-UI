namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// Outcome of a finished process run. <see cref="Cancelled"/> distinguishes a user-cancelled run
/// (tree-killed) from a normal non-zero exit. See docs/05-terraform-engine.md.
/// </summary>
public sealed record ProcessResult(
    int ExitCode,
    bool Cancelled,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public TimeSpan Duration => CompletedAt - StartedAt;

    /// <summary>A run that completed on its own with a zero exit code.</summary>
    public bool Succeeded => !Cancelled && ExitCode == 0;
}
