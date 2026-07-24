using Fenrix.IaCStudio.Domain.Files;

namespace Fenrix.IaCStudio.Contracts.Files;

/// <summary>
/// A reconciled filesystem change surfaced to the UI/editor by the synchronizer. Application-generated
/// changes (matched against the change journal) are suppressed and never surfaced here.
/// See docs/04-filesystem-sync.md.
/// </summary>
public sealed class FileSystemChangeEvent
{
    public Guid ProjectId { get; init; }
    public string RelativePath { get; init; } = string.Empty;
    public string? PreviousRelativePath { get; init; }
    public FileChangeKind ChangeKind { get; init; }

    /// <summary>True when the change came from outside Fenrix (Explorer, git, another editor).</summary>
    public bool IsExternal { get; init; }

    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;
}
