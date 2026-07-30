namespace Fenrix.IaCStudio.Domain.Enterprise;

/// <summary>
/// A role-gated approval of a specific, unchangeable deploy (replaces the Phase 9.5 local self-ack).
/// Created when a stage/policy requires approval; a <em>different</em> user holding
/// <see cref="Permission.ApproveDeployment"/> in scope records the decision. Captures the exact
/// version/commit/plan so approval is of a fixed artefact, not "whatever is current". See docs/29-enterprise.md.
/// </summary>
public sealed class ApprovalRequest
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid ProjectId { get; init; }
    public Guid EnvironmentId { get; init; }
    public string EnvironmentName { get; init; } = string.Empty;

    /// <summary>The version/plan being approved (immutable snapshot identity).</summary>
    public Guid? ProjectVersionId { get; init; }
    public string VersionLabel { get; init; } = string.Empty;
    public string GitCommit { get; init; } = string.Empty;
    public Guid? SavedPlanId { get; init; }
    public string? PlanFileHash { get; init; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    public string RequestedByKey { get; init; } = string.Empty;
    public string RequestedByName { get; init; } = string.Empty;
    public DateTimeOffset RequestedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? RequestNote { get; init; }

    /// <summary>Set once decided. Enforced to differ from the requester (separation of duties).</summary>
    public string? DecidedByKey { get; set; }
    public string? DecidedByName { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? DecisionNote { get; set; }

    /// <summary>Optional expiry; a request past this is treated as <see cref="ApprovalStatus.Expired"/>.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }
}
