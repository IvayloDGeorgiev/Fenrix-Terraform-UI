using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Domain.Settings;

/// <summary>
/// A single setting value at a given scope. Resolution is most-specific-first:
/// Environment → Project → Global → built-in default. See docs/14-settings.md.
/// </summary>
public sealed class SettingEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Key { get; set; } = string.Empty;
    public string? Value { get; set; }

    public SettingScope Scope { get; set; } = SettingScope.Global;

    /// <summary>Owner of the scope: project id for Project, environment id for Environment; null for Global.</summary>
    public Guid? ScopeId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
