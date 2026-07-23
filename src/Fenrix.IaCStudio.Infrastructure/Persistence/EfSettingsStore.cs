using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Settings;
using Microsoft.EntityFrameworkCore;

namespace Fenrix.IaCStudio.Infrastructure.Persistence;

/// <summary>EF Core implementation of <see cref="ISettingsStore"/> over <see cref="AppDbContext"/>.</summary>
public sealed class EfSettingsStore(AppDbContext db) : ISettingsStore
{
    private readonly AppDbContext _db = db;

    public async Task<IReadOnlyList<SettingEntry>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _db.Settings.AsNoTracking().ToListAsync(cancellationToken);

    public Task<SettingEntry?> FindAsync(
        string key, SettingScope scope, Guid? scopeId, CancellationToken cancellationToken = default)
        => _db.Settings.AsNoTracking().FirstOrDefaultAsync(
            s => s.Key == key && s.Scope == scope && s.ScopeId == scopeId, cancellationToken);

    public async Task UpsertAsync(SettingEntry entry, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Settings.FirstOrDefaultAsync(
            s => s.Key == entry.Key && s.Scope == entry.Scope && s.ScopeId == entry.ScopeId,
            cancellationToken);

        if (existing is null)
        {
            _db.Settings.Add(entry);
        }
        else
        {
            existing.Value = entry.Value;
            existing.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        string key, SettingScope scope, Guid? scopeId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Settings.FirstOrDefaultAsync(
            s => s.Key == key && s.Scope == scope && s.ScopeId == scopeId, cancellationToken);

        if (existing is not null)
        {
            _db.Settings.Remove(existing);
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
