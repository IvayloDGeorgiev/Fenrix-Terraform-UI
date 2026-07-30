using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Application.Enterprise;
using Fenrix.IaCStudio.Domain.Enterprise;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Enterprise;

/// <summary>
/// Seeds enterprise metadata on first run when enterprise mode is enabled: the four built-in roles, and — if
/// no role assignments exist yet — a bootstrap grant of the Administrator role (Global) to the current user, so
/// there is always someone able to manage roles and policy. Idempotent; a no-op when enterprise mode is off.
/// See docs/29-enterprise.md.
/// </summary>
public sealed class EnterpriseSeeder(
    AppDbContext db,
    IEnterpriseConfig config,
    IUserContext userContext,
    ILogger<EnterpriseSeeder> logger)
{
    private readonly AppDbContext _db = db;
    private readonly IEnterpriseConfig _config = config;
    private readonly IUserContext _userContext = userContext;
    private readonly ILogger<EnterpriseSeeder> _logger = logger;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.IsEnabled) return;

        // 1) Built-in roles (create any that are missing).
        var existing = await _db.OrgRoles.Select(r => r.Name).ToListAsync(cancellationToken);
        var have = existing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = false;
        foreach (var (name, description, permissions) in BuiltInRoles.All)
        {
            if (have.Contains(name)) continue;
            _db.OrgRoles.Add(new OrgRole
            {
                Name = name, Description = description, Permissions = permissions, IsBuiltIn = true
            });
            added = true;
        }
        if (added) await _db.SaveChangesAsync(cancellationToken);

        // 2) Bootstrap admin — only when the store has no assignments at all (fresh enterprise setup).
        if (await _db.RoleAssignments.AnyAsync(cancellationToken)) return;

        var current = _userContext.Current;
        if (!await _db.OrgUsers.AnyAsync(u => u.UserKey == current.UserKey, cancellationToken))
        {
            _db.OrgUsers.Add(new OrgUser
            {
                UserKey = current.UserKey,
                DisplayName = current.DisplayName,
                Email = current.Email,
                LastSeenAt = DateTimeOffset.UtcNow
            });
        }

        var admin = await _db.OrgRoles
            .FirstOrDefaultAsync(r => r.Name == BuiltInRoles.AdministratorName, cancellationToken);
        if (admin is null) return;

        _db.RoleAssignments.Add(new RoleAssignment
        {
            UserKey = current.UserKey,
            RoleId = admin.Id,
            Scope = AccessScopeLevel.Global,
            CreatedBy = "bootstrap"
        });
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Enterprise mode: granted bootstrap Administrator (Global) to {User}.", current.DisplayName);
    }
}
