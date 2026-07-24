namespace Fenrix.IaCStudio.Domain.Terraform;

/// <summary>
/// Lifecycle of a single command run. Persisted on <see cref="Execution.CommandRun.Status"/> as its
/// string name so the history table stays human-readable. See docs/15-logging-auditing.md.
/// </summary>
public enum TerraformRunStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,

    /// <summary>Refused before launch (e.g. the binary violates the project's version constraint).</summary>
    Blocked = 4
}
