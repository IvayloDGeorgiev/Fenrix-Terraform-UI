using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Contracts.Enterprise;

/// <summary>The outcome of an authorisation check: allowed or not, with a reason when denied.</summary>
public sealed record AuthorizationResult(bool Allowed, Permission Required, string? Reason)
{
    public static AuthorizationResult Allow(Permission required) => new(true, required, null);
    public static AuthorizationResult Deny(Permission required, string reason) => new(false, required, reason);

    /// <summary>Enterprise mode off ⇒ everything is allowed (single-user posture preserved).</summary>
    public static readonly AuthorizationResult NotEnforced = new(true, Permission.None, null);
}

/// <summary>A role as shown in the admin UI.</summary>
public sealed record RoleSummary(
    Guid Id, string Name, string? Description, Permission Permissions, bool IsBuiltIn);

/// <summary>A known user as shown in the admin UI.</summary>
public sealed record OrgUserSummary(
    Guid Id, string UserKey, string DisplayName, string? Email, bool IsEnabled, DateTimeOffset? LastSeenAt);

/// <summary>A role assignment (user ↔ role at a scope) as shown in the admin UI.</summary>
public sealed record RoleAssignmentSummary(
    Guid Id,
    string UserKey,
    string UserDisplayName,
    Guid RoleId,
    string RoleName,
    AccessScopeLevel Scope,
    Guid? ProjectId,
    string? ProjectName,
    Guid? EnvironmentId,
    string? EnvironmentName);

/// <summary>Create/update a role.</summary>
public sealed record SaveRoleRequest(Guid? Id, string Name, string? Description, Permission Permissions);

/// <summary>Assign a role to a user at a scope.</summary>
public sealed record AssignRoleRequest(
    string UserKey,
    string UserDisplayName,
    Guid RoleId,
    AccessScopeLevel Scope,
    Guid? ProjectId,
    Guid? EnvironmentId);
