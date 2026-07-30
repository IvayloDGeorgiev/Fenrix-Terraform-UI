using Fenrix.IaCStudio.Contracts.Enterprise;

namespace Fenrix.IaCStudio.Application.Abstractions.Enterprise;

/// <summary>
/// Admin CRUD for roles, users, and assignments (behind <see cref="Domain.Enterprise.Permission.ManageRoles"/>).
/// Also upserts the current user into the directory on sign-in so they can be assigned roles.
/// See docs/29-enterprise.md.
/// </summary>
public interface IRoleService
{
    Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken cancellationToken = default);
    Task<RoleSummary> SaveRoleAsync(SaveRoleRequest request, CancellationToken cancellationToken = default);
    Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<OrgUserSummary>> ListUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>Ensures an <c>OrgUser</c> row exists for the current identity (idempotent); returns it.</summary>
    Task<OrgUserSummary> EnsureCurrentUserAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RoleAssignmentSummary>> ListAssignmentsAsync(
        string? userKey = null, CancellationToken cancellationToken = default);
    Task<RoleAssignmentSummary> AssignRoleAsync(AssignRoleRequest request, CancellationToken cancellationToken = default);
    Task RemoveAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default);
}
