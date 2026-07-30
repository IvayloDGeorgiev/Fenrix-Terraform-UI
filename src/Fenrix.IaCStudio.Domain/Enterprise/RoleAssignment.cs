namespace Fenrix.IaCStudio.Domain.Enterprise;

/// <summary>
/// Binds an <see cref="OrgUser"/> to an <see cref="OrgRole"/> at a scope. A Global assignment
/// applies everywhere; a Project/Environment assignment applies only there. A user's effective
/// permissions for a request are the union of every assignment in scope. See docs/29-enterprise.md.
/// </summary>
public sealed class RoleAssignment
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>References <see cref="OrgUser.UserKey"/> (not the surrogate Id) so it survives re-imports.</summary>
    public string UserKey { get; init; } = string.Empty;
    public Guid RoleId { get; init; }

    public AccessScopeLevel Scope { get; init; } = AccessScopeLevel.Global;

    /// <summary>Set for Project/Environment scope; null for Global.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>Set for Environment scope; null otherwise.</summary>
    public Guid? EnvironmentId { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CreatedBy { get; init; } = string.Empty;
}
