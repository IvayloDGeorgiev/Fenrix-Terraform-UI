using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Application.Enterprise;
using Fenrix.IaCStudio.Contracts.Enterprise;
using Fenrix.IaCStudio.Domain.Enterprise;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fenrix.IaCStudio.Infrastructure.Enterprise;

/// <summary>
/// EF-backed role-gated approvals. Enforces separation of duties (approver ≠ requester) and the
/// <see cref="Permission.ApproveDeployment"/> requirement via the pure <see cref="ApprovalResolver"/>. Decisions
/// are audited. A governed apply checks <see cref="IsPlanApprovedAsync"/> before running. See docs/29-enterprise.md.
/// </summary>
public sealed class ApprovalService(
    AppDbContext db,
    IUserContext userContext,
    IAuthorizationService authorization,
    IAuditService audit) : IApprovalService
{
    private readonly AppDbContext _db = db;
    private readonly IUserContext _userContext = userContext;
    private readonly IAuthorizationService _authorization = authorization;
    private readonly IAuditService _audit = audit;

    public async Task<ApprovalRequestSummary> CreateAsync(
        CreateApprovalRequest request, CancellationToken cancellationToken = default)
    {
        var user = _userContext.Current;
        var entity = new ApprovalRequest
        {
            ProjectId = request.ProjectId,
            EnvironmentId = request.EnvironmentId,
            EnvironmentName = request.EnvironmentName,
            ProjectVersionId = request.ProjectVersionId,
            VersionLabel = request.VersionLabel,
            GitCommit = request.GitCommit,
            SavedPlanId = request.SavedPlanId,
            PlanFileHash = request.PlanFileHash,
            Status = ApprovalStatus.Pending,
            RequestedByKey = user.UserKey,
            RequestedByName = user.DisplayName,
            RequestNote = request.RequestNote,
            ExpiresAt = request.ExpiresAt
        };
        _db.ApprovalRequests.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(new AuditEntry(
            AuditAction.ApprovalRequested, ProjectId: request.ProjectId, EnvironmentId: request.EnvironmentId,
            EnvironmentName: request.EnvironmentName, Target: request.VersionLabel,
            Detail: "Deployment approval requested."), cancellationToken);

        return Map(entity);
    }

    public async Task<IReadOnlyList<ApprovalRequestSummary>> ListInboxAsync(CancellationToken cancellationToken = default)
    {
        var me = _userContext.Current.UserKey;
        var pending = await _db.ApprovalRequests.AsNoTracking()
            .Where(r => r.Status == ApprovalStatus.Pending && r.RequestedByKey != me)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(cancellationToken);

        // Keep only those the current user can actually approve (has the permission in that env's scope) and
        // that have not expired.
        var now = DateTimeOffset.UtcNow;
        var result = new List<ApprovalRequestSummary>();
        foreach (var r in pending)
        {
            if (ApprovalResolver.EffectiveStatus(r.Status, r.ExpiresAt, now) == ApprovalStatus.Expired) continue;
            if (await _authorization.HasPermissionAsync(
                    Permission.ApproveDeployment, r.ProjectId, r.EnvironmentId, cancellationToken))
                result.Add(Map(r));
        }
        return result;
    }

    public async Task<IReadOnlyList<ApprovalRequestSummary>> ListForProjectAsync(
        Guid projectId, CancellationToken cancellationToken = default)
    {
        var rows = await _db.ApprovalRequests.AsNoTracking()
            .Where(r => r.ProjectId == projectId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(cancellationToken);
        return rows.Select(Map).ToList();
    }

    public async Task<ApprovalRequestSummary?> GetForPlanAsync(Guid savedPlanId, CancellationToken cancellationToken = default)
    {
        var row = await _db.ApprovalRequests.AsNoTracking()
            .Where(r => r.SavedPlanId == savedPlanId)
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : Map(row);
    }

    public async Task<ApprovalDecisionResult> DecideAsync(
        ApprovalDecisionRequest request, CancellationToken cancellationToken = default)
    {
        var entity = await _db.ApprovalRequests.FirstOrDefaultAsync(r => r.Id == request.RequestId, cancellationToken);
        if (entity is null) return ApprovalDecisionResult.Fail(ApprovalStatus.Cancelled, "Request not found.");

        var now = DateTimeOffset.UtcNow;
        var effective = ApprovalResolver.EffectiveStatus(entity.Status, entity.ExpiresAt, now);
        if (effective == ApprovalStatus.Expired)
        {
            entity.Status = ApprovalStatus.Expired;
            await _db.SaveChangesAsync(cancellationToken);
            return ApprovalDecisionResult.Fail(ApprovalStatus.Expired, "This request has expired.");
        }

        var me = _userContext.Current;
        var permissions = await _authorization.GetEffectivePermissionsAsync(
            entity.ProjectId, entity.EnvironmentId, cancellationToken);

        var (allowed, reason) = ApprovalResolver.CanDecide(
            entity.Status, entity.RequestedByKey, me.UserKey, permissions);
        if (!allowed) return ApprovalDecisionResult.Fail(entity.Status, reason!);

        entity.Status = request.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        entity.DecidedByKey = me.UserKey;
        entity.DecidedByName = me.DisplayName;
        entity.DecidedAt = now;
        entity.DecisionNote = request.Note;
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.WriteAsync(new AuditEntry(
            AuditAction.ApprovalDecided, ProjectId: entity.ProjectId, EnvironmentId: entity.EnvironmentId,
            EnvironmentName: entity.EnvironmentName, Target: entity.VersionLabel,
            Detail: $"Deployment {entity.Status}."), cancellationToken);

        return new ApprovalDecisionResult(true, entity.Status, null);
    }

    public async Task<bool> IsPlanApprovedAsync(Guid savedPlanId, CancellationToken cancellationToken = default)
    {
        var row = await _db.ApprovalRequests.AsNoTracking()
            .Where(r => r.SavedPlanId == savedPlanId)
            .OrderByDescending(r => r.RequestedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is not null &&
               ApprovalResolver.AuthorisesDeploy(row.Status, row.ExpiresAt, DateTimeOffset.UtcNow);
    }

    private static ApprovalRequestSummary Map(ApprovalRequest r) => new(
        r.Id, r.ProjectId, r.EnvironmentId, r.EnvironmentName, r.VersionLabel, r.GitCommit, r.SavedPlanId,
        r.Status, r.RequestedByName, r.RequestedByKey, r.RequestedAt, r.RequestNote,
        r.DecidedByName, r.DecidedAt, r.DecisionNote, r.ExpiresAt);
}
