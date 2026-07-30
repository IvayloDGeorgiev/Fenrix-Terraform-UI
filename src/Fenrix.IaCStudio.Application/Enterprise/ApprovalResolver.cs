using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Application.Enterprise;

/// <summary>
/// Pure rules for role-gated approvals: who may decide a request, and whether a request currently authorises a
/// deploy. Separation of duties (the approver may not be the requester) and the <see cref="Permission.ApproveDeployment"/>
/// requirement are enforced here so they are unit-testable. No IO. See docs/29-enterprise.md.
/// </summary>
public static class ApprovalResolver
{
    /// <summary>Whether <paramref name="deciderKey"/> may approve/reject the request; a reason when they may not.</summary>
    public static (bool Allowed, string? Reason) CanDecide(
        ApprovalStatus status,
        string requesterKey,
        string deciderKey,
        Permission deciderEffectivePermissions)
    {
        if (status != ApprovalStatus.Pending)
            return (false, $"This request is already {status}.");
        if (string.Equals(requesterKey, deciderKey, StringComparison.Ordinal))
            return (false, "You cannot approve your own deployment request (separation of duties).");
        if (!PermissionEvaluator.Has(deciderEffectivePermissions, Permission.ApproveDeployment))
            return (false, "You need the 'ApproveDeployment' permission for this environment.");
        return (true, null);
    }

    /// <summary>True if the request currently authorises the deploy (approved, not expired).</summary>
    public static bool AuthorisesDeploy(ApprovalStatus status, DateTimeOffset? expiresAt, DateTimeOffset now)
        => status == ApprovalStatus.Approved && (expiresAt is null || expiresAt > now);

    /// <summary>Normalises a stored status to <see cref="ApprovalStatus.Expired"/> when past its expiry.</summary>
    public static ApprovalStatus EffectiveStatus(ApprovalStatus status, DateTimeOffset? expiresAt, DateTimeOffset now)
        => status == ApprovalStatus.Pending && expiresAt is { } e && e <= now ? ApprovalStatus.Expired : status;
}
