using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Contracts.Enterprise;

/// <summary>An approval request as shown in the inbox / on the deploy flow.</summary>
public sealed record ApprovalRequestSummary(
    Guid Id,
    Guid ProjectId,
    Guid EnvironmentId,
    string EnvironmentName,
    string VersionLabel,
    string GitCommit,
    Guid? SavedPlanId,
    ApprovalStatus Status,
    string RequestedByName,
    string RequestedByKey,
    DateTimeOffset RequestedAt,
    string? RequestNote,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    DateTimeOffset? ExpiresAt);

/// <summary>Create a role-gated approval request for a specific, unchangeable deploy.</summary>
public sealed record CreateApprovalRequest(
    Guid ProjectId,
    Guid EnvironmentId,
    string EnvironmentName,
    Guid? ProjectVersionId,
    string VersionLabel,
    string GitCommit,
    Guid? SavedPlanId,
    string? PlanFileHash,
    string? RequestNote,
    DateTimeOffset? ExpiresAt);

/// <summary>Approve or reject a pending request.</summary>
public sealed record ApprovalDecisionRequest(Guid RequestId, bool Approve, string? Note);

/// <summary>Outcome of a decision attempt.</summary>
public sealed record ApprovalDecisionResult(bool Succeeded, ApprovalStatus Status, string? Error)
{
    public static ApprovalDecisionResult Fail(ApprovalStatus status, string error) => new(false, status, error);
}
