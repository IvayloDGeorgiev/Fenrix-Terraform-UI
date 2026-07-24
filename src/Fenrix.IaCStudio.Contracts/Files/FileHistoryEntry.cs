using Fenrix.IaCStudio.Domain.Files;

namespace Fenrix.IaCStudio.Contracts.Files;

/// <summary>A single row in a file's history timeline, projected for the UI.</summary>
public sealed class FileHistoryEntry
{
    public Guid FileVersionId { get; set; }
    public Guid FileIdentityId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public FileChangeKind ChangeKind { get; set; }
    public ChangeOrigin Origin { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool HasContent { get; set; }
    public DateTimeOffset CapturedAt { get; set; }
}

/// <summary>A file that no longer exists on disk but whose last content is recoverable from history.</summary>
public sealed class RecoverableFile
{
    public Guid FileVersionId { get; set; }
    public Guid FileIdentityId { get; set; }
    public string RelativePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public DateTimeOffset DeletedAt { get; set; }
}
