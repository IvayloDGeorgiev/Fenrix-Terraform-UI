using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Contracts.Enterprise;
using Fenrix.IaCStudio.Domain.Enterprise;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Fenrix.IaCStudio.Infrastructure.Enterprise;

/// <summary>
/// EF-backed admin CRUD for roles, users, and assignments, plus the current-user upsert. Role/assignment
/// changes are audited. Built-in roles cannot be deleted or renamed (their permissions may be edited).
/// See docs/29-enterprise.md.
/// </summary>
public sealed class RoleService(
    AppDbContext db,
    IUserContext userContext,
    IAuthorizationService authorization,
    IAuditService audit) : IRoleService
{
    private readonly AppDbContext _db = db;
    private readonly IUserContext _userContext = userContext;
    private readonly IAuthorizationService _authorization = authorization;
    private readonly IAuditService _audit = audit;

    private async Task RequireManageRolesAsync(string target, CancellationToken ct)
    {
        var result = await _authorization.AuthorizeAsync(Permission.ManageRoles, target: target, cancellationToken: ct);
        if (!result.Allowed)
            throw new UnauthorizedAccessException(result.Reason ?? "You need the 'ManageRoles' permission.");
    }

    public async Task<IReadOnlyList<RoleSummary>> ListRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _db.OrgRoles.AsNoTracking().OrderBy(r => r.Name).ToListAsync(cancellationToken);
        return roles.Select(r => new RoleSummary(r.Id, r.Name, r.Description, r.Permissions, r.IsBuiltIn)).ToList();
    }

    public async Task<RoleSummary> SaveRoleAsync(SaveRoleRequest request, CancellationToken cancellationToken = default)
    {
        await RequireManageRolesAsync(request.Name, cancellationToken);
        OrgRole role;
        if (request.Id is { } id)
        {
            role = await _db.OrgRoles.FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                   ?? throw new InvalidOperationException("Role not found.");
            // Built-in roles keep their name; only permissions/description are editable.
            if (!role.IsBuiltIn) role.Name = request.Name.Trim();
            role.Description = request.Description?.Trim();
            role.Permissions = request.Permissions;
        }
        else
        {
            var name = request.Name.Trim();
            if (await _db.OrgRoles.AnyAsync(r => r.Name == name, cancellationToken))
                throw new InvalidOperationException($"A role named '{name}' already exists.");
            role = new OrgRole
            {
                Name = name,
                Description = request.Description?.Trim(),
                Permissions = request.Permissions
            };
            _db.OrgRoles.Add(role);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(new AuditEntry(
            AuditAction.RoleChanged, Target: role.Name,
            Detail: $"Role saved with permissions: {role.Permissions}."), cancellationToken);

        return new RoleSummary(role.Id, role.Name, role.Description, role.Permissions, role.IsBuiltIn);
    }

    public async Task DeleteRoleAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        await RequireManageRolesAsync(roleId.ToString(), cancellationToken);
        var role = await _db.OrgRoles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken);
        if (role is null) return;
        if (role.IsBuiltIn) throw new InvalidOperationException("Built-in roles cannot be deleted.");
        if (await _db.RoleAssignments.AnyAsync(a => a.RoleId == roleId, cancellationToken))
            throw new InvalidOperationException("Remove this role's assignments before deleting it.");

        _db.OrgRoles.Remove(role);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(new AuditEntry(
            AuditAction.RoleChanged, Target: role.Name, Detail: "Role deleted."), cancellationToken);
    }

    public async Task<IReadOnlyList<OrgUserSummary>> ListUsersAsync(CancellationToken cancellationToken = default)
    {
        var users = await _db.OrgUsers.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync(cancellationToken);
        return users.Select(Map).ToList();
    }

    public async Task<OrgUserSummary> EnsureCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        var current = _userContext.Current;
        var user = await _db.OrgUsers.FirstOrDefaultAsync(u => u.UserKey == current.UserKey, cancellationToken);
        if (user is null)
        {
            user = new OrgUser
            {
                UserKey = current.UserKey,
                DisplayName = current.DisplayName,
                Email = current.Email,
                LastSeenAt = DateTimeOffset.UtcNow
            };
            _db.OrgUsers.Add(user);
        }
        else
        {
            user.DisplayName = current.DisplayName;
            if (!string.IsNullOrWhiteSpace(current.Email)) user.Email = current.Email;
            user.LastSeenAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return Map(user);
    }

    public async Task<IReadOnlyList<RoleAssignmentSummary>> ListAssignmentsAsync(
        string? userKey = null, CancellationToken cancellationToken = default)
    {
        var q = _db.RoleAssignments.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(userKey)) q = q.Where(a => a.UserKey == userKey);

        var assignments = await q.ToListAsync(cancellationToken);
        if (assignments.Count == 0) return [];

        // Resolve display names in a few batched lookups (assignment lists are small).
        var roleNames = await _db.OrgRoles.AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);
        var userNames = await _db.OrgUsers.AsNoTracking()
            .ToDictionaryAsync(u => u.UserKey, u => u.DisplayName, cancellationToken);
        var projectNames = await _db.Projects.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Name, cancellationToken);
        var envNames = await _db.Environments.AsNoTracking()
            .ToDictionaryAsync(e => e.Id, e => e.Name, cancellationToken);

        return assignments.Select(a => new RoleAssignmentSummary(
            a.Id, a.UserKey,
            userNames.GetValueOrDefault(a.UserKey, a.UserKey),
            a.RoleId, roleNames.GetValueOrDefault(a.RoleId, "(deleted role)"),
            a.Scope,
            a.ProjectId, a.ProjectId is { } p ? projectNames.GetValueOrDefault(p) : null,
            a.EnvironmentId, a.EnvironmentId is { } e ? envNames.GetValueOrDefault(e) : null))
            .ToList();
    }

    public async Task<RoleAssignmentSummary> AssignRoleAsync(
        AssignRoleRequest request, CancellationToken cancellationToken = default)
    {
        await RequireManageRolesAsync(request.UserDisplayName, cancellationToken);
        if (!await _db.OrgRoles.AnyAsync(r => r.Id == request.RoleId, cancellationToken))
            throw new InvalidOperationException("Role not found.");

        ValidateScope(request.Scope, request.ProjectId, request.EnvironmentId);

        // Make sure the target user is in the directory so the name resolves later.
        if (!await _db.OrgUsers.AnyAsync(u => u.UserKey == request.UserKey, cancellationToken))
        {
            _db.OrgUsers.Add(new OrgUser
            {
                UserKey = request.UserKey,
                DisplayName = string.IsNullOrWhiteSpace(request.UserDisplayName)
                    ? request.UserKey : request.UserDisplayName
            });
        }

        // Avoid duplicate identical assignments.
        var existing = await _db.RoleAssignments.FirstOrDefaultAsync(a =>
            a.UserKey == request.UserKey && a.RoleId == request.RoleId &&
            a.Scope == request.Scope && a.ProjectId == request.ProjectId &&
            a.EnvironmentId == request.EnvironmentId, cancellationToken);

        var assignment = existing ?? new RoleAssignment
        {
            UserKey = request.UserKey,
            RoleId = request.RoleId,
            Scope = request.Scope,
            ProjectId = request.ProjectId,
            EnvironmentId = request.EnvironmentId,
            CreatedBy = _userContext.Current.DisplayName
        };
        if (existing is null) _db.RoleAssignments.Add(assignment);

        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(new AuditEntry(
            AuditAction.RoleChanged, ProjectId: request.ProjectId, EnvironmentId: request.EnvironmentId,
            Target: request.UserDisplayName,
            Detail: $"Assigned role at {request.Scope} scope."), cancellationToken);

        var list = await ListAssignmentsAsync(request.UserKey, cancellationToken);
        return list.First(x => x.Id == assignment.Id);
    }

    public async Task RemoveAssignmentAsync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        await RequireManageRolesAsync(assignmentId.ToString(), cancellationToken);
        var assignment = await _db.RoleAssignments.FirstOrDefaultAsync(a => a.Id == assignmentId, cancellationToken);
        if (assignment is null) return;

        _db.RoleAssignments.Remove(assignment);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync(new AuditEntry(
            AuditAction.RoleChanged, ProjectId: assignment.ProjectId, EnvironmentId: assignment.EnvironmentId,
            Target: assignment.UserKey, Detail: "Assignment removed."), cancellationToken);
    }

    private static void ValidateScope(AccessScopeLevel scope, Guid? projectId, Guid? environmentId)
    {
        switch (scope)
        {
            case AccessScopeLevel.Global when projectId is null && environmentId is null:
            case AccessScopeLevel.Project when projectId is not null:
            case AccessScopeLevel.Environment when environmentId is not null:
                return;
            default:
                throw new InvalidOperationException($"Scope {scope} requires the matching project/environment id.");
        }
    }

    private static OrgUserSummary Map(OrgUser u) =>
        new(u.Id, u.UserKey, u.DisplayName, u.Email, u.IsEnabled, u.LastSeenAt);
}
