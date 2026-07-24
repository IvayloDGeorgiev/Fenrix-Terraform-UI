namespace Fenrix.IaCStudio.Domain.Files;

/// <summary>
/// An immutable snapshot of a tracked file at a point in time. Content lives in a
/// deduplicated <see cref="FileBlob"/> referenced by <see cref="BlobId"/> (null for
/// deletion markers). See docs/21-file-history-recovery.md and ADR-0004.
/// </summary>
public sealed class FileVersion
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid ProjectId { get; init; }

    /// <summary>Project-relative path at the time of capture.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Groups versions of the same file across renames.</summary>
    public Guid FileIdentityId { get; init; }

    public FileChangeKind ChangeKind { get; init; }

    /// <summary>SHA-256 of the original (uncompressed) content; empty for deletion markers.</summary>
    public string ContentHash { get; init; } = string.Empty;
    public long SizeBytes { get; init; }

    /// <summary>The content-addressed blob; null for deletion markers or when unchanged.</summary>
    public Guid? BlobId { get; init; }

    public string? GitCommit { get; init; }
    public string? GitBranch { get; init; }

    public ChangeOrigin Origin { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
}
