using Fenrix.IaCStudio.Contracts.Files;

namespace Fenrix.IaCStudio.Application.Abstractions.Files;

/// <summary>
/// Provider-neutral file version history. Works against whichever database is connected (SQLite by
/// default, SQL Server optionally) because it goes through EF Core only. See docs/21-file-history-recovery.md.
/// </summary>
public interface IFileHistoryStore
{
    /// <summary>Records a version for an observed change (after an atomic write or detected external change).</summary>
    Task RecordAsync(FileChange change, CancellationToken ct = default);

    /// <summary>The version timeline for a file identity, newest first.</summary>
    Task<IReadOnlyList<FileHistoryEntry>> GetHistoryAsync(Guid fileIdentityId, CancellationToken ct = default);

    /// <summary>The version timeline for a file by its current relative path, newest first.</summary>
    Task<IReadOnlyList<FileHistoryEntry>> GetHistoryForPathAsync(Guid projectId, string relativePath, CancellationToken ct = default);

    /// <summary>Opens the (decompressed) content of a specific version. Throws if the version has no blob.</summary>
    Task<Stream> OpenContentAsync(Guid fileVersionId, CancellationToken ct = default);

    /// <summary>Files that were deleted (in-app or externally) and can be restored, newest deletion first.</summary>
    Task<IReadOnlyList<RecoverableFile>> GetRecoverableAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Writes a version's content back to disk atomically and records a Restored version.</summary>
    Task RestoreAsync(Guid fileVersionId, string targetFullPath, CancellationToken ct = default);

    /// <summary>
    /// Permanently discards the recoverable content for one deleted file (its identity + versions, freeing any
    /// blobs no longer referenced). Only affects an already-deleted item; live-file history is untouched.
    /// </summary>
    Task PurgeRecoverableItemAsync(Guid fileIdentityId, CancellationToken ct = default);

    /// <summary>
    /// Permanently discards every recoverable (deleted) file for a project. Returns how many were purged.
    /// Live files and their history are untouched.
    /// </summary>
    Task<int> PurgeAllRecoverableAsync(Guid projectId, CancellationToken ct = default);
}
