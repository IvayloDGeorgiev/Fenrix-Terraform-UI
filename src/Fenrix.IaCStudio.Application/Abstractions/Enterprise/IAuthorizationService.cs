using Fenrix.IaCStudio.Contracts.Enterprise;
using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Application.Abstractions.Enterprise;

/// <summary>
/// Answers "may the current user do X here?" by unioning their in-scope role grants. When enterprise mode
/// is off, everything is allowed (the prior single-user posture). A denied <see cref="AuthorizeAsync"/>
/// writes an audit row. Enforced at every safety-relevant call site. See docs/29-enterprise.md, ADR-0006.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>True if the current user holds <paramref name="permission"/> for the target scope.</summary>
    Task<bool> HasPermissionAsync(
        Permission permission, Guid? projectId = null, Guid? environmentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>The current user's effective (unioned) permissions for the target scope.</summary>
    Task<Permission> GetEffectivePermissionsAsync(
        Guid? projectId = null, Guid? environmentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="HasPermissionAsync"/> but returns a typed result with a reason and, on denial, records
    /// an <see cref="AuditAction.AuthorizationDenied"/> audit event. Callers use this at guarded actions.
    /// </summary>
    Task<AuthorizationResult> AuthorizeAsync(
        Permission permission, Guid? projectId = null, Guid? environmentId = null,
        string? target = null, CancellationToken cancellationToken = default);
}
