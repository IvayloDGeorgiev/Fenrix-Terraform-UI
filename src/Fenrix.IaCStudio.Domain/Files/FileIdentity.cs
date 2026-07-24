namespace Fenrix.IaCStudio.Domain.Files;

/// <summary>
/// Groups all <see cref="FileVersion"/> records that belong to the "same" file across renames,
/// so a file's timeline survives a move. The identity is keyed within a project.
/// See docs/21-file-history-recovery.md.
/// </summary>
public sealed class FileIdentity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }

    /// <summary>The current project-relative path (updated on rename/move).</summary>
    public string CurrentRelativePath { get; set; } = string.Empty;

    /// <summary>True once the file has been deleted on disk and only survives in history.</summary>
    public bool IsDeleted { get; set; }

    public DateTimeOffset FirstSeenAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastChangedAt { get; set; } = DateTimeOffset.UtcNow;
}
