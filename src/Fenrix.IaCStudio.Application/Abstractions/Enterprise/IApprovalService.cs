using Fenrix.IaCStudio.Contracts.Enterprise;

namespace Fenrix.IaCStudio.Application.Abstractions.Enterprise;

/// <summary>
/// Role-gated deployment approvals (replaces the Phase 9.5 local self-ack). A request captures the exact
/// version/commit/plan; a <em>different</em> user holding <see cref="Domain.Enterprise.Permission.ApproveDeployment"/>
/// in scope decides it. A governed apply consults <see cref="IsPlanApprovedAsync"/> before proceeding.
/// See docs/29-enterprise.md, docs/20-pipelines-deployments.md.
/// </summary>
public interface IApprovalService
{
    Task<ApprovalRequestSummary> CreateAsync(CreateApprovalRequest request, CancellationToken cancellationToken = default);

    /// <summary>Pending requests the current user is allowed to decide (has the permission and isn't the requester).</summary>
    Task<IReadOnlyList<ApprovalRequestSummary>> ListInboxAsync(CancellationToken cancellationToken = default);

    /// <summary>All requests for a project (any status), newest first — for history/board views.</summary>
    Task<IReadOnlyList<ApprovalRequestSummary>> ListForProjectAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<ApprovalRequestSummary?> GetForPlanAsync(Guid savedPlanId, CancellationToken cancellationToken = default);

    Task<ApprovalDecisionResult> DecideAsync(ApprovalDecisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>True when a valid (approved, unexpired) request exists for the saved plan.</summary>
    Task<bool> IsPlanApprovedAsync(Guid savedPlanId, CancellationToken cancellationToken = default);
}
