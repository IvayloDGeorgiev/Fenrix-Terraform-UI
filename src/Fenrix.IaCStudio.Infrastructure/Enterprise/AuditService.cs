using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Contracts.Enterprise;
using Fenrix.IaCStudio.Domain.Enterprise;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Enterprise;

/// <summary>
/// EF-backed central audit sink + reader. Writes a redacted <see cref="AuditEvent"/> stamped with the current
/// user; best-effort so a logging failure never breaks the audited action. See docs/15-logging-auditing.md,
/// docs/29-enterprise.md.
/// </summary>
public sealed class AuditService(
    AppDbContext db,
    IUserContext userContext,
    ILogger<AuditService> logger) : IAuditService
{
    private readonly AppDbContext _db = db;
    private readonly IUserContext _userContext = userContext;
    private readonly ILogger<AuditService> _logger = logger;

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = _userContext.Current;
            _db.AuditEvents.Add(new AuditEvent
            {
                Action = entry.Action,
                Outcome = entry.Outcome,
                UserKey = user.UserKey,
                UserDisplayName = user.DisplayName,
                ProjectId = entry.ProjectId,
                ProjectName = Truncate(entry.ProjectName, 200),
                EnvironmentId = entry.EnvironmentId,
                EnvironmentName = Truncate(entry.EnvironmentName, 120),
                Target = Truncate(entry.Target, 1024),
                Detail = Truncate(entry.Detail, 2048),
                OccurredAt = DateTimeOffset.UtcNow
            });
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Never let auditing break the action it records.
            _logger.LogWarning(ex, "Failed to write audit event {Action}.", entry.Action);
        }
    }

    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var q = _db.AuditEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.UserKey)) q = q.Where(e => e.UserKey == query.UserKey);
        if (query.ProjectId is { } pid) q = q.Where(e => e.ProjectId == pid);
        if (query.Action is { } action) q = q.Where(e => e.Action == action);
        if (query.Outcome is { } outcome) q = q.Where(e => e.Outcome == outcome);
        if (query.From is { } from) q = q.Where(e => e.OccurredAt >= from);
        if (query.To is { } to) q = q.Where(e => e.OccurredAt <= to);

        var total = await q.CountAsync(cancellationToken);
        var take = Math.Clamp(query.Take, 1, 1000);
        var skip = Math.Max(0, query.Skip);

        var rows = await q
            .OrderByDescending(e => e.OccurredAt)
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken);

        return new AuditPage(rows.Select(Map).ToList(), total);
    }

    private static AuditEventSummary Map(AuditEvent e) => new(
        e.Id, e.Action, e.Outcome, e.UserDisplayName, e.UserKey,
        e.ProjectId, e.ProjectName, e.EnvironmentId, e.EnvironmentName,
        e.Target, e.Detail, e.OccurredAt);

    private static string? Truncate(string? value, int max)
        => value is null || value.Length <= max ? value : value[..max];
}
