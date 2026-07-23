namespace Fenrix.IaCStudio.Domain.Execution;

/// <summary>
/// Redacted history of a tool invocation (Terraform, Git, cloud CLI). Arguments are
/// stored redacted; raw sensitive output is never persisted. See docs/15-logging-auditing.md.
/// </summary>
public sealed class CommandRun
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid? ProjectId { get; init; }
    public Guid? EnvironmentId { get; init; }

    public string Tool { get; init; } = string.Empty;       // terraform | git | az | aws | gcloud
    public string Command { get; init; } = string.Empty;
    public string RedactedArguments { get; init; } = string.Empty;
    public string WorkingDirectory { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
    public int? ExitCode { get; set; }
    public string Status { get; set; } = "Running";
    public string? OutputLogPath { get; set; }
}
