using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Settings;

namespace Fenrix.IaCStudio.Application.Abstractions;

/// <summary>
/// Persistence port for settings. Implemented in Infrastructure over EF Core.
/// The Application layer depends on this abstraction, not on the database.
/// </summary>
public interface ISettingsStore
{
    Task<IReadOnlyList<SettingEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<SettingEntry?> FindAsync(
        string key, SettingScope scope, Guid? scopeId, CancellationToken cancellationToken = default);

    Task UpsertAsync(SettingEntry entry, CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string key, SettingScope scope, Guid? scopeId, CancellationToken cancellationToken = default);
}
