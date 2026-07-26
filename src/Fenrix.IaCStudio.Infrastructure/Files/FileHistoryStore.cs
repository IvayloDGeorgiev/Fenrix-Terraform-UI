using System.IO.Compression;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Application.Files;
using Fenrix.IaCStudio.Contracts.Files;
using Fenrix.IaCStudio.Domain.Files;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Files;

/// <summary>
/// EF Core implementation of the version store. Content is GZip-compressed and deduplicated by
/// SHA-256 hash across all files/versions. Works against any connected provider (SQLite/SQL Server).
/// See docs/21-file-history-recovery.md and ADR-0004.
/// </summary>
public sealed class FileHistoryStore(
    AppDbContext db,
    IChangeJournal journal,
    ILogger<FileHistoryStore> logger) : IFileHistoryStore
{
    private readonly AppDbContext _db = db;
    private readonly IChangeJournal _journal = journal;
    private readonly ILogger<FileHistoryStore> _logger = logger;

    public async Task RecordAsync(FileChange change, CancellationToken ct = default)
    {
        if (!FileTrackingPolicy.IsVersioned(change.RelativePath))
            return;

        var lookupPath = change.PreviousRelativePath ?? change.RelativePath;
        var identity = await GetOrCreateIdentityAsync(change.ProjectId, lookupPath, ct);

        // Follow renames: point the identity at the new path.
        if (change.ChangeKind == FileChangeKind.Renamed || !string.Equals(identity.CurrentRelativePath, change.RelativePath, StringComparison.Ordinal))
            identity.CurrentRelativePath = change.RelativePath;
        identity.LastChangedAt = DateTimeOffset.UtcNow;

        if (change.ChangeKind == FileChangeKind.DeletedDetected)
        {
            identity.IsDeleted = true;
            _db.FileVersions.Add(new FileVersion
            {
                ProjectId = change.ProjectId,
                RelativePath = change.RelativePath,
                FileIdentityId = identity.Id,
                ChangeKind = FileChangeKind.DeletedDetected,
                ContentHash = string.Empty,
                SizeBytes = 0,
                BlobId = null,
                Origin = change.Origin,
                GitCommit = change.GitCommit,
                GitBranch = change.GitBranch
            });
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("Recorded deletion of {Path} (project {Project})", change.RelativePath, change.ProjectId);
            return;
        }

        if (string.IsNullOrEmpty(change.FullPath) || !File.Exists(change.FullPath))
            return;

        var content = await File.ReadAllBytesAsync(change.FullPath, ct);
        var hash = FileHashing.Sha256Hex(content);

        // Skip if the latest version for this identity already has this content.
        var latestHash = await _db.FileVersions
            .Where(v => v.FileIdentityId == identity.Id)
            .OrderByDescending(v => v.CapturedAt)
            .Select(v => v.ContentHash)
            .FirstOrDefaultAsync(ct);
        if (latestHash == hash && change.ChangeKind != FileChangeKind.Renamed)
        {
            identity.IsDeleted = false;
            await _db.SaveChangesAsync(ct);
            return;
        }

        var blob = await GetOrCreateBlobAsync(hash, content, ct);
        identity.IsDeleted = false;

        _db.FileVersions.Add(new FileVersion
        {
            ProjectId = change.ProjectId,
            RelativePath = change.RelativePath,
            FileIdentityId = identity.Id,
            ChangeKind = change.ChangeKind,
            ContentHash = hash,
            SizeBytes = content.LongLength,
            BlobId = blob.Id,
            Origin = change.Origin,
            GitCommit = change.GitCommit,
            GitBranch = change.GitBranch
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<FileHistoryEntry>> GetHistoryAsync(Guid fileIdentityId, CancellationToken ct = default)
    {
        return await _db.FileVersions
            .Where(v => v.FileIdentityId == fileIdentityId)
            .OrderByDescending(v => v.CapturedAt)
            .Select(v => new FileHistoryEntry
            {
                FileVersionId = v.Id,
                FileIdentityId = v.FileIdentityId,
                RelativePath = v.RelativePath,
                ChangeKind = v.ChangeKind,
                Origin = v.Origin,
                ContentHash = v.ContentHash,
                SizeBytes = v.SizeBytes,
                HasContent = v.BlobId != null,
                CapturedAt = v.CapturedAt
            })
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<FileHistoryEntry>> GetHistoryForPathAsync(Guid projectId, string relativePath, CancellationToken ct = default)
    {
        var normalized = relativePath.Replace('\\', '/');
        var identityId = await _db.FileIdentities
            .Where(i => i.ProjectId == projectId && i.CurrentRelativePath == normalized)
            .Select(i => (Guid?)i.Id)
            .FirstOrDefaultAsync(ct);

        return identityId is null
            ? []
            : await GetHistoryAsync(identityId.Value, ct);
    }

    public async Task<Stream> OpenContentAsync(Guid fileVersionId, CancellationToken ct = default)
    {
        var version = await _db.FileVersions.FirstOrDefaultAsync(v => v.Id == fileVersionId, ct)
            ?? throw new InvalidOperationException($"File version {fileVersionId} not found.");

        if (version.BlobId is null)
            throw new InvalidOperationException("This version is a deletion marker and has no content.");

        var blob = await _db.FileBlobs.FirstAsync(b => b.Id == version.BlobId, ct);
        return new MemoryStream(Decompress(blob.Data), writable: false);
    }

    public async Task<IReadOnlyList<RecoverableFile>> GetRecoverableAsync(Guid projectId, CancellationToken ct = default)
    {
        var deletedIdentities = await _db.FileIdentities
            .Where(i => i.ProjectId == projectId && i.IsDeleted)
            .Select(i => i.Id)
            .ToListAsync(ct);

        var result = new List<RecoverableFile>();
        foreach (var identityId in deletedIdentities)
        {
            // Last version carrying real content, plus the deletion timestamp.
            var lastContent = await _db.FileVersions
                .Where(v => v.FileIdentityId == identityId && v.BlobId != null)
                .OrderByDescending(v => v.CapturedAt)
                .FirstOrDefaultAsync(ct);
            if (lastContent is null)
                continue;

            var deletedAt = await _db.FileVersions
                .Where(v => v.FileIdentityId == identityId && v.ChangeKind == FileChangeKind.DeletedDetected)
                .OrderByDescending(v => v.CapturedAt)
                .Select(v => v.CapturedAt)
                .FirstOrDefaultAsync(ct);

            result.Add(new RecoverableFile
            {
                FileVersionId = lastContent.Id,
                FileIdentityId = identityId,
                RelativePath = lastContent.RelativePath,
                SizeBytes = lastContent.SizeBytes,
                ContentHash = lastContent.ContentHash,
                DeletedAt = deletedAt == default ? lastContent.CapturedAt : deletedAt
            });
        }

        return result.OrderByDescending(r => r.DeletedAt).ToList();
    }

    public async Task RestoreAsync(Guid fileVersionId, string targetFullPath, CancellationToken ct = default)
    {
        var version = await _db.FileVersions.FirstOrDefaultAsync(v => v.Id == fileVersionId, ct)
            ?? throw new InvalidOperationException($"File version {fileVersionId} not found.");
        if (version.BlobId is null)
            throw new InvalidOperationException("Cannot restore a deletion marker.");

        var blob = await _db.FileBlobs.FirstAsync(b => b.Id == version.BlobId, ct);
        var content = Decompress(blob.Data);

        Directory.CreateDirectory(Path.GetDirectoryName(targetFullPath)!);
        // Journal the write first so the watcher recognises it as app-generated.
        _journal.Record(targetFullPath, FileChangeKind.Restored, content.LongLength, version.ContentHash);
        await AtomicWriteAsync(targetFullPath, content, ct);

        var identity = await _db.FileIdentities.FirstAsync(i => i.Id == version.FileIdentityId, ct);
        identity.IsDeleted = false;
        identity.LastChangedAt = DateTimeOffset.UtcNow;

        _db.FileVersions.Add(new FileVersion
        {
            ProjectId = version.ProjectId,
            RelativePath = version.RelativePath,
            FileIdentityId = version.FileIdentityId,
            ChangeKind = FileChangeKind.Restored,
            ContentHash = version.ContentHash,
            SizeBytes = version.SizeBytes,
            BlobId = version.BlobId,
            Origin = ChangeOrigin.Restore
        });

        // Restoring reuses an existing blob → bump its refcount.
        blob.RefCount += 1;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation("Restored {Path} from version {Version}", targetFullPath, fileVersionId);
    }

    public async Task PurgeRecoverableItemAsync(Guid fileIdentityId, CancellationToken ct = default)
    {
        var identity = await _db.FileIdentities.FirstOrDefaultAsync(i => i.Id == fileIdentityId, ct);
        // Only purge a genuinely-deleted (recoverable) item; never touch a live file's history.
        if (identity is null || !identity.IsDeleted)
            return;
        await PurgeIdentitiesAsync([fileIdentityId], ct);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> PurgeAllRecoverableAsync(Guid projectId, CancellationToken ct = default)
    {
        var deletedIdentityIds = await _db.FileIdentities
            .Where(i => i.ProjectId == projectId && i.IsDeleted)
            .Select(i => i.Id)
            .ToListAsync(ct);

        if (deletedIdentityIds.Count == 0)
            return 0;

        await PurgeIdentitiesAsync(deletedIdentityIds, ct);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Purged {Count} recoverable item(s) for project {Project}", deletedIdentityIds.Count, projectId);
        return deletedIdentityIds.Count;
    }

    /// <summary>
    /// Removes the versions + identities for the given deleted files, decrementing each referenced blob's
    /// refcount and deleting blobs that drop to zero (they may be shared via SHA-256 dedup). Caller saves.
    /// </summary>
    private async Task PurgeIdentitiesAsync(IReadOnlyList<Guid> identityIds, CancellationToken ct)
    {
        var versions = await _db.FileVersions
            .Where(v => identityIds.Contains(v.FileIdentityId))
            .ToListAsync(ct);

        var blobDecrements = versions
            .Where(v => v.BlobId != null)
            .GroupBy(v => v.BlobId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        _db.FileVersions.RemoveRange(versions);

        if (blobDecrements.Count > 0)
        {
            var blobIds = blobDecrements.Keys.ToList();
            var blobs = await _db.FileBlobs.Where(b => blobIds.Contains(b.Id)).ToListAsync(ct);
            foreach (var blob in blobs)
            {
                blob.RefCount -= blobDecrements[blob.Id];
                if (blob.RefCount <= 0)
                    _db.FileBlobs.Remove(blob);
            }
        }

        var identities = await _db.FileIdentities.Where(i => identityIds.Contains(i.Id)).ToListAsync(ct);
        _db.FileIdentities.RemoveRange(identities);
    }

    // ---- helpers ----

    private async Task<FileIdentity> GetOrCreateIdentityAsync(Guid projectId, string relativePath, CancellationToken ct)
    {
        var normalized = relativePath.Replace('\\', '/');
        var identity = await _db.FileIdentities
            .FirstOrDefaultAsync(i => i.ProjectId == projectId && i.CurrentRelativePath == normalized, ct);

        if (identity is not null)
            return identity;

        identity = new FileIdentity { ProjectId = projectId, CurrentRelativePath = normalized };
        _db.FileIdentities.Add(identity);
        return identity;
    }

    private async Task<FileBlob> GetOrCreateBlobAsync(string hash, byte[] content, CancellationToken ct)
    {
        var existing = await _db.FileBlobs.FirstOrDefaultAsync(b => b.ContentHash == hash, ct);
        if (existing is not null)
        {
            existing.RefCount += 1;
            return existing;
        }

        var blob = new FileBlob
        {
            ContentHash = hash,
            Data = Compress(content),
            OriginalSize = content.LongLength,
            RefCount = 1
        };
        _db.FileBlobs.Add(blob);
        return blob;
    }

    private static byte[] Compress(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(content, 0, content.Length);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] compressed)
    {
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    private static async Task AtomicWriteAsync(string fullPath, byte[] content, CancellationToken ct)
    {
        var temp = fullPath + ".fenrixtmp";
        await File.WriteAllBytesAsync(temp, content, ct);
        File.Move(temp, fullPath, overwrite: true);
    }
}
