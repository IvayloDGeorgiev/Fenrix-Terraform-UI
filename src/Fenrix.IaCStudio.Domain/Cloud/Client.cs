namespace Fenrix.IaCStudio.Domain.Cloud;

/// <summary>
/// A customer / account group that owns connections (and optionally projects).
/// Organises the connections library at scale. See docs/26-connections.md.
/// </summary>
public sealed class Client
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Description { get; set; }
    public List<string> Tags { get; set; } = [];
}
