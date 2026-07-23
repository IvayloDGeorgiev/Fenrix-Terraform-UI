using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Application.Settings;

/// <summary>
/// Reads and writes settings with most-specific-first resolution
/// (Environment → Project → Global → default). See docs/14-settings.md.
/// </summary>
public interface ISettingsService
{
    /// <summary>
    /// Resolves a setting value, walking Environment → Project → Global.
    /// Returns null if unset at every scope.
    /// </summary>
    Task<string?> GetAsync(
        string key, Guid? projectId = null, Guid? environmentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves a setting value, or returns <paramref name="fallback"/> if unset.</summary>
    Task<string> GetOrDefaultAsync(
        string key, string fallback, Guid? projectId = null, Guid? environmentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Resolves and parses a typed setting, or returns <paramref name="fallback"/>.</summary>
    Task<T> GetOrDefaultAsync<T>(
        string key, T fallback, Guid? projectId = null, Guid? environmentId = null,
        CancellationToken cancellationToken = default) where T : IParsable<T>;

    /// <summary>Sets a value at a scope. Pass the owning id for Project/Environment scope.</summary>
    Task SetAsync(
        string key, string? value, SettingScope scope = SettingScope.Global, Guid? scopeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>Clears a value at a specific scope (does not affect other scopes).</summary>
    Task ClearAsync(
        string key, SettingScope scope, Guid? scopeId = null,
        CancellationToken cancellationToken = default);
}
