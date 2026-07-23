# 21 · File Version History & Recovery

Fenrix keeps a **version snapshot of every file change** in the connected database so that accidental loss — an overwrite, or a deletion from Windows Explorer or another tool — is recoverable from inside the app. This is a local safety net on top of Git, not a replacement for it, and it works against **whichever database is connected** (SQLite by default, SQL Server optionally).

> **Reconciling with the source-of-truth rule ([ADR-0002](adr/0002-files-as-source-of-truth.md)).** Files on disk remain authoritative. The version store is a **recovery cache / local history**, not the working copy: Fenrix always reads and writes the real files first, then records a snapshot. If the DB and disk ever disagree, disk wins for the *current* state; the DB only supplies *previous* versions on explicit request. See the amendment in [ADR-0002](adr/0002-files-as-source-of-truth.md) and [ADR-0004](adr/0004-db-file-version-history.md).

## What is captured

Every time Fenrix observes a **create** or **update** to a tracked file — whether the change came from Fenrix's own editor or was detected on disk by the watcher/reconciler ([04-filesystem-sync.md](04-filesystem-sync.md)) — it records a new version:

- The file's project-relative path.
- The change type (Created, Updated, Renamed, Deleted-detected).
- Content hash + size.
- The content itself (see [storage](#storage--size-management)).
- Timestamp and origin (Fenrix editor / external / import).
- Optional link to the Git commit and current branch, if known.

Deletions are **recorded** (so the last-known content is retained for recovery) but see the deletion policy below.

## Deletion policy

- **Deletion from *within* the app is restricted.** The app does not offer hard delete of tracked Terraform files. Removing a file uses the Recycle Bin ([04-filesystem-sync.md](04-filesystem-sync.md)) *and* the last version is retained in the DB, so it is always recoverable. (Configurable in Settings → Security; a project can opt into permitted deletes with confirmation.)
- **Deletion *outside* the app** (Explorer, git, another editor) is *detected* by the reconciler and recorded as a `Deleted-detected` event. The previous content stays in the version store, and the file appears in **Recoverable items** with a one-click restore.

This directly satisfies the goal: *if a file is deleted by accident it is recoverable from the database.*

## Data model

```csharp
public sealed class FileVersion
{
    public Guid Id { get; init; }
    public Guid ProjectId { get; init; }
    public string RelativePath { get; init; } = "";   // stable key across renames via FileIdentityId
    public Guid FileIdentityId { get; init; }           // groups versions of the "same" file across renames

    public FileChangeKind ChangeKind { get; init; }     // Created, Updated, Renamed, DeletedDetected, Restored
    public string ContentHash { get; init; } = "";
    public long SizeBytes { get; init; }

    public Guid? BlobId { get; init; }                  // null for deletion markers / unchanged
    public bool IsCompressed { get; init; }

    public string? GitCommit { get; init; }
    public string? GitBranch { get; init; }

    public ChangeOrigin Origin { get; init; }           // FenrixEditor, External, Import, Restore
    public DateTimeOffset CapturedAt { get; init; }
}

public sealed class FileBlob            // content-addressed; deduplicated by hash
{
    public Guid Id { get; init; }
    public string ContentHash { get; init; } = "";      // unique
    public byte[] Data { get; init; } = [];             // compressed payload
    public long OriginalSize { get; init; }
    public int RefCount { get; init; }                  // how many FileVersions point here
}
```

Content is **content-addressed and deduplicated**: identical content across versions/files stores one blob. Renames keep history via `FileIdentityId` so a file's timeline survives a rename.

## Storage & size management

- Only **text/config files** relevant to Terraform projects are versioned by default: `.tf`, `.tfvars`, `.hcl`, `.json`, `.md`, `.gitignore`, backend/config files. Binary and generated content is excluded (respecting the ignored-directory list — `.terraform\`, `bin\`, `obj\`, `node_modules\`, `.git\`).
- Content is **compressed** and **deduplicated by hash** before storage.
- **Retention policy** (Settings → Security/Database): keep all versions for N days, then thin to milestones (e.g. keep first + last per day), always keeping the last-known content of any deleted file until the project is removed. Large files above a configurable threshold store a hash + pointer rather than inline content, with a warning.
- A background pruning job enforces retention and drops orphaned blobs (`RefCount == 0`).

These controls keep the version store bounded so it stays a safety net, not a runaway table.

## Database-agnostic design

The version store is defined entirely in the shared `AppDbContext` and works against **any connected provider** ([12-database-design.md](12-database-design.md)):

- **SQLite** (default) — blobs stored in-DB (`BLOB` column) or as external files under `Data\` referenced by hash, chosen by a size threshold to keep the DB file healthy.
- **SQL Server / Azure SQL** (optional) — same schema; blobs in `VARBINARY(MAX)` or `FILESTREAM`/external per configuration.

Because access goes through EF Core and a provider-neutral `IFileHistoryStore` abstraction, switching databases requires no code changes — only configuration. Team/enterprise setups can therefore share file history centrally on SQL Server while individuals use SQLite.

```csharp
public interface IFileHistoryStore
{
    Task RecordAsync(FileChange change, CancellationToken ct);         // called after every atomic write / detected change
    Task<IReadOnlyList<FileVersion>> GetHistoryAsync(Guid fileIdentityId, CancellationToken ct);
    Task<Stream> OpenContentAsync(Guid fileVersionId, CancellationToken ct);
    Task<IReadOnlyList<FileVersion>> GetRecoverableAsync(Guid projectId, CancellationToken ct); // deleted / removed
    Task<FileVersion> RestoreAsync(Guid fileVersionId, string targetPath, CancellationToken ct); // writes back to disk
}
```

## UI

- **File history panel** (in the editor / Files view): a timeline of versions for the current file, with diff-against-current, preview, and "restore this version." Restores write back to disk atomically and record a `Restored` version — never a silent overwrite.
- **Recoverable items** view (per project): files that were deleted (in-app or externally) with last-known content and one-click restore.
- Restores and deletions are **audit events** ([15-logging-auditing.md](15-logging-auditing.md)): `file version restored`, `file deletion detected`, `file recovered`.

## Security

Versioned content can contain sensitive values (e.g. a committed-by-mistake secret in a `.tfvars`). The version store is treated as project data: it lives in the same database and is subject to the same redaction rules for *logs*, but the **stored file content is the real content** (that is the point of recovery). Therefore: never surface stored versions in logs or diagnostics exports, keep the DB itself protected by OS/db permissions, and honour the project's ignore rules so secret files the user chose to ignore are not swept in. See [11-secrets.md](11-secrets.md).

## Relationship to Git and deployments

- **Git** remains the shared, intentional version history (commits, branches, remotes). File history is a **local, automatic, fine-grained** safety net between commits — it captures the save you never committed.
- **Deployments** ([20-pipelines-deployments.md](20-pipelines-deployments.md)) reference Git commits for *what was deployed*; file history answers *what did this file look like at 14:05 before I broke it*. The two are complementary.

## Delivery placement

Foundational capture (record + recover deleted files) fits alongside **Phase 2** (project management + filesystem watcher), since it hooks the same write/detect pipeline. Richer history UI, retention thinning, and SQL Server sharing follow with the persistence work (Phases 1/11). Tracked in [ROADMAP.md](ROADMAP.md) / [PROGRESS.md](PROGRESS.md).
