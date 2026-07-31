using System.Globalization;
using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Enterprise;
using Fenrix.IaCStudio.Application.Abstractions.Maintenance;
using Fenrix.IaCStudio.Contracts.Maintenance;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Maintenance;

/// <summary>
/// SQLite-backed implementation of <see cref="IBackupService"/> (Phase 12). Snapshots the local metadata
/// database with SQLite's online backup API, keeps a bounded history under <c>Backups/</c>, stages restores to
/// apply on next launch, and tracks a session marker for crash detection. No-ops (skips) when the metadata
/// store is an external SQL Server. See docs/12-database-design.md, docs/18-packaging-deployment.md.
/// </summary>
public sealed class SqliteBackupService : IBackupService
{
    // File name shape: fenrix-backup-{utcSortable}-{reason}.db  (sortable so newest sorts last).
    private const string Prefix = "fenrix-backup-";
    private const string Extension = ".db";
    private const int RetentionCount = 10;
    private const string TimeFormat = "yyyyMMdd-HHmmss";

    private readonly IWorkspacePaths _paths;
    private readonly IEnterpriseConfig _enterprise;
    private readonly ILogger<SqliteBackupService> _logger;

    public SqliteBackupService(
        IWorkspacePaths paths,
        IEnterpriseConfig enterprise,
        ILogger<SqliteBackupService> logger)
    {
        _paths = paths;
        _enterprise = enterprise;
        _logger = logger;
    }

    private bool IsExternalStore =>
        string.Equals(_enterprise.MetadataProvider, "SqlServer", StringComparison.OrdinalIgnoreCase);

    private string BackupsDir => _paths.BackupsDirectory;
    private string DbPath => _paths.DatabaseFilePath;
    private string SessionMarkerPath => Path.Combine(_paths.DataDirectory, "session.marker");
    private string PendingRestorePath => Path.Combine(_paths.DataDirectory, "restore.pending");

    public async Task<BackupResult> CreateBackupAsync(BackupReason reason, CancellationToken cancellationToken = default)
    {
        if (IsExternalStore)
            return BackupResult.Skip("Metadata store is an external SQL Server; back it up with your DBA tooling.");

        if (!File.Exists(DbPath))
            return BackupResult.Skip("No database file exists yet; nothing to back up.");

        try
        {
            Directory.CreateDirectory(BackupsDir);
            var createdAt = DateTimeOffset.Now;
            var id = $"{Prefix}{createdAt.UtcDateTime.ToString(TimeFormat, CultureInfo.InvariantCulture)}-{reason}";
            var destination = Path.Combine(BackupsDir, id + Extension);

            // Online backup: consistent even while the app holds the database open (handles WAL). Read-only
            // source connection; the backup connection creates the destination file.
            await Task.Run(() =>
            {
                // Pooling=False so neither connection leaves a lingering handle on the database file — the very
                // next startup step (EF migrate) opens it for writing.
                using var source = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Pooling=False");
                using var dest = new SqliteConnection($"Data Source={destination};Pooling=False");
                source.Open();
                dest.Open();
                source.BackupDatabase(dest);
            }, cancellationToken).ConfigureAwait(false);

            var info = Describe(destination);
            _logger.LogInformation("Database backup written ({Reason}) to {Path} ({Size} bytes).",
                reason, destination, info?.SizeBytes ?? 0);

            Prune();
            return info is null
                ? BackupResult.Fail("Backup file was not found after writing.")
                : BackupResult.Ok(info);
        }
        catch (Exception ex)
        {
            // Best-effort: never let a backup failure block startup or an operation.
            _logger.LogWarning(ex, "Database backup failed ({Reason}).", reason);
            return BackupResult.Fail(ex.Message);
        }
    }

    public IReadOnlyList<BackupInfo> ListBackups()
    {
        if (!Directory.Exists(BackupsDir))
            return [];

        return Directory.EnumerateFiles(BackupsDir, Prefix + "*" + Extension)
            .Select(Describe)
            .Where(b => b is not null)
            .Select(b => b!)
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    public async Task<BackupResult> RestoreAsync(string backupId, CancellationToken cancellationToken = default)
    {
        if (IsExternalStore)
            return BackupResult.Skip("Metadata store is an external SQL Server; restore it with your DBA tooling.");

        var backup = ListBackups().FirstOrDefault(b => b.Id == backupId);
        if (backup is null || !File.Exists(backup.FilePath))
            return BackupResult.Fail("The selected backup no longer exists.");

        try
        {
            // 1) Safety copy of the current database so a restore is itself reversible.
            if (File.Exists(DbPath))
                await CreateBackupAsync(BackupReason.PreRestore, cancellationToken).ConfigureAwait(false);

            // 2) Stage the swap. Applying it now could race live connections; ApplyPendingRestoreAsync performs
            //    the copy at next launch before the context opens.
            await File.WriteAllTextAsync(PendingRestorePath, backup.FilePath, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Restore staged from {Path}; will apply on next launch.", backup.FilePath);
            return new BackupResult(true, backup, false, "Restore staged — restart to apply.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Staging restore from {Id} failed.", backupId);
            return BackupResult.Fail(ex.Message);
        }
    }

    public async Task<BackupInfo?> ApplyPendingRestoreAsync(CancellationToken cancellationToken = default)
    {
        if (IsExternalStore || !File.Exists(PendingRestorePath))
            return null;

        try
        {
            var source = (await File.ReadAllTextAsync(PendingRestorePath, cancellationToken).ConfigureAwait(false)).Trim();
            if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
            {
                // Self-heal: a dangling pointer should never wedge startup.
                TryDelete(PendingRestorePath);
                return null;
            }

            var info = Describe(source);

            // Ensure no stale WAL/SHM sidecars from the outgoing database survive alongside the restored file.
            Directory.CreateDirectory(_paths.DataDirectory);
            File.Copy(source, DbPath, overwrite: true);
            TryDelete(DbPath + "-wal");
            TryDelete(DbPath + "-shm");
            TryDelete(PendingRestorePath);

            _logger.LogInformation("Applied staged restore from {Path}.", source);
            return info;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Applying staged restore failed; leaving the current database in place.");
            TryDelete(PendingRestorePath);
            return null;
        }
    }

    public async Task BeginSessionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(_paths.DataDirectory);
            var marker = string.Join('\n',
                $"pid={Environment.ProcessId}",
                $"startedAt={DateTimeOffset.Now:O}");
            await File.WriteAllTextAsync(SessionMarkerPath, marker, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not write the session marker; crash detection may be unavailable.");
        }
    }

    public void EndSession() => TryDelete(SessionMarkerPath);

    public async Task<CrashRecoveryReport> InspectCrashStateAsync(CancellationToken cancellationToken = default)
    {
        var latest = ListBackups().FirstOrDefault();

        if (!File.Exists(SessionMarkerPath))
            return new CrashRecoveryReport(false, null, latest);

        DateTimeOffset? startedAt = null;
        try
        {
            foreach (var line in await File.ReadAllLinesAsync(SessionMarkerPath, cancellationToken).ConfigureAwait(false))
            {
                if (line.StartsWith("startedAt=", StringComparison.Ordinal)
                    && DateTimeOffset.TryParse(line["startedAt=".Length..], CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out var ts))
                {
                    startedAt = ts;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read the previous session marker.");
        }

        _logger.LogWarning("Previous session did not shut down cleanly (marker present).");
        return new CrashRecoveryReport(true, startedAt, latest);
    }

    /// <summary>Keeps the newest <see cref="RetentionCount"/> backups and deletes the rest (oldest first).</summary>
    private void Prune()
    {
        try
        {
            var all = ListBackups(); // newest first
            foreach (var stale in all.Skip(RetentionCount))
                TryDelete(stale.FilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pruning old backups failed (non-fatal).");
        }
    }

    private static BackupInfo? Describe(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (!fi.Exists) return null;

            var id = Path.GetFileNameWithoutExtension(path);
            var reason = ParseReason(id);
            return new BackupInfo(id, fi.FullName, reason, new DateTimeOffset(fi.LastWriteTime), fi.Length);
        }
        catch
        {
            return null;
        }
    }

    private static BackupReason ParseReason(string id)
    {
        var dash = id.LastIndexOf('-');
        if (dash >= 0 && dash < id.Length - 1
            && Enum.TryParse<BackupReason>(id[(dash + 1)..], ignoreCase: true, out var reason))
        {
            return reason;
        }
        return BackupReason.Manual;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best-effort */ }
    }
}
