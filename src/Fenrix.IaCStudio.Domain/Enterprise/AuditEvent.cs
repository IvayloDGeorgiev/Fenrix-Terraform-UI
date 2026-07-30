namespace Fenrix.IaCStudio.Domain.Enterprise;

/// <summary>
/// An append-only record of who did what safety-relevant action, when, against which
/// project/environment, and whether it was allowed or blocked. Redacted before persistence:
/// holds only summaries/identifiers — never secrets, plan JSON, or key material.
/// Centralised in the metadata DB in enterprise mode. See docs/15-logging-auditing.md, docs/29-enterprise.md.
/// </summary>
public sealed class AuditEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public AuditAction Action { get; init; }
    public AuditOutcome Outcome { get; init; } = AuditOutcome.Allowed;

    /// <summary>Stable identity key of the actor (<see cref="OrgUser.UserKey"/>).</summary>
    public string UserKey { get; init; } = string.Empty;
    public string UserDisplayName { get; init; } = string.Empty;

    public Guid? ProjectId { get; init; }
    public string? ProjectName { get; init; }
    public Guid? EnvironmentId { get; init; }
    public string? EnvironmentName { get; init; }

    /// <summary>The target of the action (e.g. a resource address, a version label, a setting key). Redacted.</summary>
    public string? Target { get; init; }

    /// <summary>A short, already-redacted human summary. Never raw command output or secrets.</summary>
    public string? Detail { get; init; }

    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
}
