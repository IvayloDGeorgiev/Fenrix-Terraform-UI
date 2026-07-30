namespace Fenrix.IaCStudio.Domain.Enterprise;

/// <summary>
/// A named bundle of <see cref="Permission"/>s. Four defaults are seeded (Viewer, Operator,
/// Approver, Administrator); teams can add their own. <see cref="IsBuiltIn"/> roles cannot be
/// deleted. See docs/29-enterprise.md.
/// </summary>
public sealed class OrgRole
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public Permission Permissions { get; set; } = Permission.None;

    /// <summary>Seeded defaults that cannot be deleted (their permissions may still be edited by an admin).</summary>
    public bool IsBuiltIn { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
