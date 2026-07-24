namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// A redacted row of command-run history for display. Arguments are already redacted; the raw output
/// lives in a log file referenced by <see cref="OutputLogPath"/>. See docs/15-logging-auditing.md and
/// docs/23-command-transparency.md.
/// </summary>
public sealed record CommandRunSummary(
    Guid Id,
    Guid? ProjectId,
    Guid? EnvironmentId,
    string Tool,
    string Command,
    string RedactedArguments,
    string WorkingDirectory,
    string Status,
    int? ExitCode,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? OutputLogPath)
{
    public TimeSpan? Duration => CompletedAt is null ? null : CompletedAt - StartedAt;

    /// <summary>The full command line as it ran, redacted and copyable.</summary>
    public string DisplayCommand =>
        string.IsNullOrEmpty(RedactedArguments) ? Tool : $"{Tool} {RedactedArguments}";
}
