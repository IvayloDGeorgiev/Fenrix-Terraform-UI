using Fenrix.IaCStudio.Domain.Files;

namespace Fenrix.IaCStudio.Contracts.Files;

/// <summary>
/// Describes a single observed change to record in the file-history store. Produced by the
/// editor after an atomic write and by the reconciler for external changes.
/// See docs/21-file-history-recovery.md.
/// </summary>
public sealed class FileChange
{
    public Guid ProjectId { get; set; }

    /// <summary>Project-relative path (forward slashes).</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Absolute path on disk (used to read content for capture); null for deletions.</summary>
    public string? FullPath { get; set; }

    public FileChangeKind ChangeKind { get; set; }
    public ChangeOrigin Origin { get; set; }

    /// <summary>For renames: the previous project-relative path, so history can follow the identity.</summary>
    public string? PreviousRelativePath { get; set; }

    public string? GitCommit { get; set; }
    public string? GitBranch { get; set; }
}
