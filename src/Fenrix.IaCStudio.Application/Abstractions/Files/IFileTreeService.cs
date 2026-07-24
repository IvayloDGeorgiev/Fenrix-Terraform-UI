using Fenrix.IaCStudio.Contracts.Files;

namespace Fenrix.IaCStudio.Application.Abstractions.Files;

/// <summary>
/// File-tree operations for a project. All mutating operations use atomic writes and route through
/// the change journal (loop prevention) and the history store (recovery). Deletes prefer the
/// Recycle Bin. See docs/04-filesystem-sync.md.
/// </summary>
public interface IFileTreeService
{
    /// <summary>Builds the current tree from disk (source of truth), collapsing ignored directories.</summary>
    Task<FileTreeNode> GetTreeAsync(Guid projectId, string projectRoot, CancellationToken ct = default);

    /// <summary>Creates an empty file (atomic). Fails if it already exists.</summary>
    Task CreateFileAsync(Guid projectId, string projectRoot, string relativePath, string? initialContent = null, CancellationToken ct = default);

    /// <summary>Creates a folder (and any missing parents).</summary>
    Task CreateFolderAsync(string projectRoot, string relativePath, CancellationToken ct = default);

    /// <summary>Writes file content atomically (temp file + replace) and records a version.</summary>
    Task WriteFileAsync(Guid projectId, string projectRoot, string relativePath, string content, CancellationToken ct = default);

    /// <summary>Renames a file or folder within the project, preserving history via the identity.</summary>
    Task RenameAsync(Guid projectId, string projectRoot, string relativePath, string newName, CancellationToken ct = default);

    /// <summary>Moves a file or folder to a new relative directory within the project.</summary>
    Task MoveAsync(Guid projectId, string projectRoot, string relativePath, string newRelativePath, CancellationToken ct = default);

    /// <summary>
    /// Deletes a file or folder, preferring the Recycle Bin. In-app delete of tracked files is
    /// restricted unless enabled in Settings; the last version is always retained for recovery.
    /// </summary>
    Task DeleteAsync(Guid projectId, string projectRoot, string relativePath, CancellationToken ct = default);
}
