namespace Fenrix.IaCStudio.Domain.Projects;

/// <summary>A recently opened file with cursor position, for restoring editor state.</summary>
public sealed class RecentFile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }
    public string Path { get; set; } = string.Empty;
    public DateTimeOffset LastOpenedAt { get; set; } = DateTimeOffset.UtcNow;
    public int CursorLine { get; set; }
    public int CursorColumn { get; set; }
}
