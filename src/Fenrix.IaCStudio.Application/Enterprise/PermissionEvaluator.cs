using Fenrix.IaCStudio.Domain.Enterprise;

namespace Fenrix.IaCStudio.Application.Enterprise;

/// <summary>
/// A single scoped grant: the permissions a role confers, plus the scope its assignment applies at.
/// </summary>
public readonly record struct ScopedGrant(
    AccessScopeLevel Scope, Guid? ProjectId, Guid? EnvironmentId, Permission Permissions);

/// <summary>
/// Pure RBAC policy: given a user's scoped grants and the request's target, compute the effective
/// permissions and answer permission checks. Grant-based and additive across scopes (a Global grant is
/// never lost when narrowing) — matching most-specific-first only in that narrower scopes can add more.
/// No I/O, so it is unit-testable without a database. See docs/29-enterprise.md.
/// </summary>
public static class PermissionEvaluator
{
    /// <summary>The union of every grant that applies to the given target scope.</summary>
    public static Permission Effective(
        IEnumerable<ScopedGrant> grants, Guid? projectId, Guid? environmentId)
    {
        var result = Permission.None;
        foreach (var g in grants)
            if (AppliesTo(g, projectId, environmentId))
                result |= g.Permissions;
        return result;
    }

    /// <summary>True when <paramref name="effective"/> contains every bit of <paramref name="required"/>.</summary>
    public static bool Has(Permission effective, Permission required)
        => (effective & required) == required;

    /// <summary>Convenience: does the grant set confer <paramref name="required"/> at the target scope?</summary>
    public static bool Has(
        IEnumerable<ScopedGrant> grants, Permission required, Guid? projectId, Guid? environmentId)
        => Has(Effective(grants, projectId, environmentId), required);

    private static bool AppliesTo(ScopedGrant g, Guid? projectId, Guid? environmentId)
        => g.Scope switch
        {
            // Global grants apply to every request.
            AccessScopeLevel.Global => true,
            // A project grant applies to requests targeting that project (or an environment within it, when
            // the caller supplies the owning project id alongside the environment id).
            AccessScopeLevel.Project => projectId is not null && g.ProjectId == projectId,
            // An environment grant applies only to requests targeting that exact environment.
            AccessScopeLevel.Environment => environmentId is not null && g.EnvironmentId == environmentId,
            _ => false
        };
}
