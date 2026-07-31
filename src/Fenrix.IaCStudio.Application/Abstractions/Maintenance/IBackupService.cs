using Fenrix.IaCStudio.Contracts.Maintenance;

namespace Fenrix.IaCStudio.Application.Abstractions.Maintenance;

/// <summary>
/// Database backup, restore, and crash-recovery for release builds (Phase 12). Backups are point-in-time
/// snapshots of the local SQLite metadata database written under the data root's <c>Backups/</c> directory,
/// with a bounded retention. When the metadata store is an external SQL Server (enterprise mode), file-level
/// backup does not apply and the service reports a skip — such stores are backed up by the DBA out of band.
///
/// <para>Nothing here holds a secret: the database stores only references (Windows Credential Manager / DPAPI),
/// never credential values, so a backup file is no more sensitive than the live database — see docs/11-secrets.md.</para>
///
/// <para>See docs/12-database-design.md, docs/18-packaging-deployment.md.</para>
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Takes a consistent snapshot of the live database using SQLite's online backup API (safe while the
    /// database is open and WAL-journalled), then prunes older backups beyond the retention limit. Best-effort:
    /// returns a failure result rather than throwing so startup is never blocked by a backup problem.
    /// </summary>
    Task<BackupResult> CreateBackupAsync(BackupReason reason, CancellationToken cancellationToken = default);

    /// <summary>Lists available backups, newest first.</summary>
    IReadOnlyList<BackupInfo> ListBackups();

    /// <summary>
    /// Stages a restore from a backup. A <see cref="BackupReason.PreRestore"/> safety copy of the current
    /// database is taken first, then the chosen backup is recorded as pending. The swap itself is applied by
    /// <see cref="ApplyPendingRestoreAsync"/> at the next launch, before the <c>AppDbContext</c> is opened, so
    /// the live database file is never overwritten while connections may hold it. The UI should prompt for a
    /// restart after this returns.
    /// </summary>
    Task<BackupResult> RestoreAsync(string backupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a restore staged by <see cref="RestoreAsync"/>, if one is pending. Call once at the very start of
    /// startup, before opening the database. Returns the backup that was applied, or null if none was pending.
    /// Best-effort and self-healing: a corrupt/missing pending pointer is cleared rather than throwing.
    /// </summary>
    Task<BackupInfo?> ApplyPendingRestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the start of a session by writing a session marker (records PID + start time). Call once at
    /// startup, after <see cref="InspectCrashStateAsync"/>.
    /// </summary>
    Task BeginSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the session marker, signalling a clean shutdown. Best-effort.</summary>
    void EndSession();

    /// <summary>
    /// Reads the session marker left by the previous run (if any) to determine whether the last session ended
    /// cleanly, and surfaces the latest backup available to restore. Call once at startup before
    /// <see cref="BeginSessionAsync"/>.
    /// </summary>
    Task<CrashRecoveryReport> InspectCrashStateAsync(CancellationToken cancellationToken = default);
}
