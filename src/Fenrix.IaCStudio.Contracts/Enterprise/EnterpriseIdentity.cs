namespace Fenrix.IaCStudio.Contracts.Enterprise;

/// <summary>
/// The resolved current user. On a Windows desktop this is the OS user (SID + name); a future
/// identity provider (Entra/OIDC) yields the same shape with a verified subject. See docs/29-enterprise.md.
/// </summary>
public sealed record CurrentUser(
    string UserKey,
    string DisplayName,
    string? Email,
    bool IsAuthenticated)
{
    /// <summary>A safe placeholder used before identity resolves (should not normally be seen).</summary>
    public static readonly CurrentUser Unknown = new("unknown", "Unknown", null, false);
}

/// <summary>Which metadata backend is active and whether enterprise governance is on. Read-only, surfaced in Settings.</summary>
public sealed record EnterpriseStatus(
    bool Enabled,
    string MetadataProvider,   // "Sqlite" | "SqlServer"
    string? Organisation,
    bool ConnectionResolved)
{
    public static readonly EnterpriseStatus LocalSqlite = new(false, "Sqlite", null, true);
}
