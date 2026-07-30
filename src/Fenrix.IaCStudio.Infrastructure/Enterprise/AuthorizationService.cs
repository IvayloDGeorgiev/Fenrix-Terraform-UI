using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Application.Enterprise;
using Fenrix.IaCStudio.Contracts.Enterprise;
using Fenrix.IaCStudio.Domain.Enterprise;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fenrix.IaCStudio.Infrastructure.Enterprise;

/// <summary>
/// EF-backed authorisation. When enterprise mode is off, everything is allowed (single-user posture). When on,
/// the current user's in-scope role grants are unioned via the pure <see cref="PermissionEvaluator"/>. A denied
/// <see cref="AuthorizeAsync"/> records an <see cref="AuditAction.AuthorizationDenied"/> event.
/// See docs/29-enterprise.md, ADR-0006.
/// </summary>
public sealed class AuthorizationService(
    AppDbContext db,
    IEnterpriseConfig config,
    IUserContext userContext,
    IAuditService audit) : IAuthorizationService
{
    private readonly AppDbContext _db = db;
    private readonly IEnterpriseConfig _config = config;
    private readonly IUserContext _userContext = userContext;
    private readonly IAuditService _audit = audit;

    public async Task<bool> HasPermissionAsync(
        Permission permission, Guid? projectId = null, Guid? environmentId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_config.IsEnabled) return true;
        var effective = await LoadEffectiveAsync(projectId, environmentId, cancellationToken);
        return PermissionEvaluator.Has(effective, permission);
    }

    public async Task<Permission> GetEffectivePermissionsAsync(
        Guid? projectId = null, Guid? environmentId = null, CancellationToken cancellationToken = default)
    {
        if (!_config.IsEnabled) return Permission.All;
        return await LoadEffectiveAsync(projectId, environmentId, cancellationToken);
    }

    public async Task<AuthorizationResult> AuthorizeAsync(
        Permission permission, Guid? projectId = null, Guid? environmentId = null,
        string? target = null, CancellationToken cancellationToken = default)
    {
        if (!_config.IsEnabled) return AuthorizationResult.NotEnforced;

        if (await HasPermissionAsync(permission, projectId, environmentId, cancellationToken))
            return AuthorizationResult.Allow(permission);

        var reason = $"You need the '{permission}' permission" +
                     (environmentId is not null ? " for this environment." :
                      projectId is not null ? " for this project." : ".");

        await _audit.WriteAsync(new AuditEntry(
            AuditAction.AuthorizationDenied, AuditOutcome.Blocked,
            ProjectId: projectId, EnvironmentId: environmentId,
            Target: target, Detail: reason), cancellationToken);

        return AuthorizationResult.Deny(permission, reason);
    }

    private async Task<Permission> LoadEffectiveAsync(
        Guid? projectId, Guid? environmentId, CancellationToken ct)
    {
        var key = _userContext.Current.UserKey;

        var rows = await (
            from a in _db.RoleAssignments.AsNoTracking()
            join r in _db.OrgRoles.AsNoTracking() on a.RoleId equals r.Id
            where a.UserKey == key
            select new { a.Scope, a.ProjectId, a.EnvironmentId, r.Permissions })
            .ToListAsync(ct);

        var grants = rows.Select(x => new ScopedGrant(x.Scope, x.ProjectId, x.EnvironmentId, x.Permissions));
        return PermissionEvaluator.Effective(grants, projectId, environmentId);
    }
}
