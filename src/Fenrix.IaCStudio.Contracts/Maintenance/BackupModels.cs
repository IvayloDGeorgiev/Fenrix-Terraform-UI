namespace Fenrix.IaCStudio.Contracts.Maintenance;

/// <summary>Why a backup was taken — drives naming and retention. See docs/18-packaging-deployment.md (Phase 12).</summary>
public enum BackupReason
{
    /// <summary>Routine snapshot taken on a clean startup.</summary>
    Startup,

    /// <summary>Taken immediately before applying pending EF migrations on a version upgrade.</summary>
    PreMigration,

    /// <summary>Safety copy of the live database taken immediately before a restore overwrites it.</summary>
    PreRestore,

    /// <summary>User asked for a backup explicitly (Settings → Maintenance).</summary>
    Manual,
}

/// <summary>A backup file on disk under the data root's <c>Backups/</c> directory.</summary>
/// <param name="Id">Stable identifier (the backup file name without extension).</param>
/// <param name="FilePath">Full path to the backup file.</param>
/// <param name="Reason">Why it was taken.</param>
/// <param name="CreatedAt">When it was written.</param>
/// <param name="SizeBytes">On-disk size.</param>
public sealed record BackupInfo(
    string Id,
    string FilePath,
    BackupReason Reason,
    DateTimeOffset CreatedAt,
    long SizeBytes);

/// <summary>Outcome of a backup attempt.</summary>
/// <param name="Succeeded">True if a backup file was written (false when skipped or failed).</param>
/// <param name="Backup">The backup that was written, when <paramref name="Succeeded"/> is true.</param>
/// <param name="Skipped">True when backup does not apply (e.g. an external SQL Server metadata store).</param>
/// <param name="Message">Human-readable detail (skip reason or error), never a secret.</param>
public sealed record BackupResult(
    bool Succeeded,
    BackupInfo? Backup,
    bool Skipped,
    string? Message)
{
    public static BackupResult Ok(BackupInfo backup) => new(true, backup, false, null);
    public static BackupResult Skip(string message) => new(false, null, true, message);
    public static BackupResult Fail(string message) => new(false, null, false, message);
}

/// <summary>
/// The result of the startup crash check. The app writes a session marker on launch and removes it on a clean
/// shutdown; a marker still present at the next launch means the previous session ended unexpectedly.
/// </summary>
/// <param name="UncleanShutdown">True if the previous session did not shut down cleanly.</param>
/// <param name="PreviousSessionStartedAt">When the previous (crashed) session started, if known.</param>
/// <param name="LatestBackup">The most recent backup available to restore, if any.</param>
public sealed record CrashRecoveryReport(
    bool UncleanShutdown,
    DateTimeOffset? PreviousSessionStartedAt,
    BackupInfo? LatestBackup);
