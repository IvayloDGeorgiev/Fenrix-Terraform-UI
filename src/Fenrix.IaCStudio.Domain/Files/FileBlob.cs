namespace Fenrix.IaCStudio.Domain.Files;

/// <summary>
/// Content-addressed, compressed file content, deduplicated by hash. Multiple
/// <see cref="FileVersion"/> rows (across files and time) can point at one blob.
/// See docs/21-file-history-recovery.md.
/// </summary>
public sealed class FileBlob
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>SHA-256 of the original content. Unique — the dedup key.</summary>
    public string ContentHash { get; init; } = string.Empty;

    /// <summary>Stored payload (GZip-compressed).</summary>
    public byte[] Data { get; init; } = [];

    /// <summary>Size of the original (uncompressed) content in bytes.</summary>
    public long OriginalSize { get; init; }

    /// <summary>How many <see cref="FileVersion"/> rows reference this blob; pruned when it reaches zero.</summary>
    public int RefCount { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
