namespace Fenrix.IaCStudio.Domain.Enterprise;

/// <summary>
/// A known identity in the organisation metadata store. <see cref="UserKey"/> is the stable
/// identity key (the Windows SID today; an OIDC subject later) that role assignments and audit
/// rows reference. Holds no secret. See docs/29-enterprise.md, ADR-0006.
/// </summary>
public sealed class OrgUser
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Stable identity key — Windows SID (or OIDC subject in a future identity provider).</summary>
    public string UserKey { get; init; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSeenAt { get; set; }
}
