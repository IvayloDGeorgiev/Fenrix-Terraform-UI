using Fenrix.IaCStudio.Domain.Terraform;

namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>
/// A redacted, display-ready view of a persisted saved plan and its safety metadata. The raw plan JSON is
/// never stored — only these summary counts, hashes, and provenance. See docs/06-plan-apply-safety.md.
/// </summary>
public sealed record SavedPlanSummary(
    Guid Id,
    Guid ProjectId,
    Guid EnvironmentId,
    string EnvironmentName,
    PlanMode Mode,
    string PlanFilePath,
    string? RelativePlanFilePath,
    string? TerraformVersion,
    int AddCount,
    int ChangeCount,
    int DestroyCount,
    int ReplaceCount,
    bool IsProductionTarget,
    bool Applied,
    DateTimeOffset CreatedAt,
    DateTimeOffset? AppliedAt,
    bool IsInvalidated,
    string? InvalidatedReason)
{
    public bool HasDeletions => DestroyCount > 0;
    public bool HasReplacements => ReplaceCount > 0;

    /// <summary>True when this plan is still a valid, un-applied candidate for apply.</summary>
    public bool CanApply => !Applied && !IsInvalidated;

    /// <summary>Whether the plan represents any state-changing work.</summary>
    public bool HasChanges => AddCount + ChangeCount + DestroyCount + ReplaceCount > 0;
}
