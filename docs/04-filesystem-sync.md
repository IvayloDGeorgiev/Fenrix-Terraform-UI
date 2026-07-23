# 04 · Filesystem Synchronisation

**Source-of-truth rule:** the physical filesystem is authoritative; the database is an index and cache (see [ADR-0002](adr/0002-files-as-source-of-truth.md)).

## Changes made inside Fenrix

When a user creates, renames, moves, updates, or deletes a file or folder, Fenrix immediately performs the corresponding filesystem operation using **atomic writes**:

1. Write new content to a temporary file.
2. Flush it.
3. Replace the original file.
4. Record the successful result.
5. Notify the editor and Git status services.

## Changes made in Windows or another editor

Use `FileSystemWatcher` for immediate notifications **combined with periodic directory reconciliation**. Reconciliation is required because OS watcher events can be combined, reordered, duplicated, missed during heavy activity, or fired multiple times for a single save.

```csharp
public interface IProjectFileSynchronizer
{
    Task StartAsync(string projectPath, CancellationToken cancellationToken);
    Task StopAsync(string projectPath);
    Task<FileTreeSnapshot> RescanAsync(string projectPath, CancellationToken cancellationToken);
}
```

Reconciliation compares a fresh directory snapshot (paths + content hashes) against the index and emits add/update/delete/rename deltas, correcting anything the watcher missed.

## Loop prevention

When Fenrix writes a file it records a short-lived **change journal** entry containing the absolute path, operation type, timestamp, expected file length, and expected content hash. Watcher events matching a journal entry are recognised as application-generated and are not surfaced as external changes. Entries expire after a short window.

## Ignored directories

By default, do not deeply monitor:

```text
.git\
.terraform\
node_modules\
bin\
obj\
.fenrix\artifacts\
```

These are noisy and machine-generated; watching them wastes cycles and causes event storms. Exclusions are configurable in Settings (Advanced → File watcher exclusions).

## File deletion

Prefer the Windows Recycle Bin over permanent deletion. Before deleting Terraform files, show the file path, its Git status, whether it contains resources, and whether it has unsaved changes, so the user understands the impact.

## Conflict handling

If both the index/editor buffer and the on-disk file changed since last sync (detected via content hash), Fenrix prompts the user rather than overwriting silently — offering to keep the disk version, keep the editor version, or open a diff.
