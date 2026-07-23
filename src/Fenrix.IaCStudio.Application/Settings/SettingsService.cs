using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Settings;

namespace Fenrix.IaCStudio.Application.Settings;

/// <summary>
/// Default <see cref="ISettingsService"/>. Resolution order is most-specific-first:
/// Environment → Project → Global → built-in default. See docs/14-settings.md.
/// </summary>
public sealed class SettingsService(ISettingsStore store) : ISettingsService
{
    private readonly ISettingsStore _store = store;

    public async Task<string?> GetAsync(
        string key, Guid? projectId = null, Guid? environmentId = null,
        CancellationToken cancellationToken = default)
    {
        if (environmentId is { } envId)
        {
            var env = await _store.FindAsync(key, SettingScope.Environment, envId, cancellationToken);
            if (env?.Value is not null) return env.Value;
        }

        if (projectId is { } projId)
        {
            var proj = await _store.FindAsync(key, SettingScope.Project, projId, cancellationToken);
            if (proj?.Value is not null) return proj.Value;
        }

        var global = await _store.FindAsync(key, SettingScope.Global, null, cancellationToken);
        return global?.Value;
    }

    public async Task<string> GetOrDefaultAsync(
        string key, string fallback, Guid? projectId = null, Guid? environmentId = null,
        CancellationToken cancellationToken = default)
        => await GetAsync(key, projectId, environmentId, cancellationToken) ?? fallback;

    public async Task<T> GetOrDefaultAsync<T>(
        string key, T fallback, Guid? projectId = null, Guid? environmentId = null,
        CancellationToken cancellationToken = default) where T : IParsable<T>
    {
        var raw = await GetAsync(key, projectId, environmentId, cancellationToken);
        return raw is not null && T.TryParse(raw, null, out var parsed) ? parsed : fallback;
    }

    public Task SetAsync(
        string key, string? value, SettingScope scope = SettingScope.Global, Guid? scopeId = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new SettingEntry
        {
            Key = key,
            Value = value,
            Scope = scope,
            ScopeId = scope == SettingScope.Global ? null : scopeId,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        return _store.UpsertAsync(entry, cancellationToken);
    }

    public Task ClearAsync(
        string key, SettingScope scope, Guid? scopeId = null,
        CancellationToken cancellationToken = default)
        => _store.DeleteAsync(key, scope, scope == SettingScope.Global ? null : scopeId, cancellationToken);
}
