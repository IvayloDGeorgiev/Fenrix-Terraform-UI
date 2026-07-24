namespace Fenrix.IaCStudio.Contracts.Files;

/// <summary>A node in a project's file tree. Built from a live directory scan (disk is source of truth).</summary>
public sealed class FileTreeNode
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Project-relative path (forward slashes), stable key for UI and history.</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Absolute path on disk.</summary>
    public string FullPath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    /// <summary>True when the node is under an ignored directory (e.g. <c>.terraform</c>) and shown greyed/collapsed.</summary>
    public bool IsIgnored { get; set; }

    public long SizeBytes { get; set; }
    public DateTimeOffset? ModifiedAt { get; set; }

    public List<FileTreeNode> Children { get; set; } = [];
}
