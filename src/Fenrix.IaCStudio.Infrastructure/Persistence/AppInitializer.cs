using System.Data;
using System.Data.Common;
using Fenrix.IaCStudio.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Persistence;

/// <summary>
/// Creates the workspace tree, then brings the database schema up to date using EF Core migrations.
///
/// <para>Handles three cases without ever destroying data (see docs/12-database-design.md):</para>
/// <list type="number">
///   <item>A database already under migration control → apply any pending migrations incrementally.</item>
///   <item>A fresh/empty database → create the schema from migrations.</item>
///   <item>A legacy database created by <c>EnsureCreated</c> (schema present, no
///     <c>__EFMigrationsHistory</c>) → <em>adopt</em> it: create any tables the model needs that are not
///     yet on disk, then stamp the existing migrations as applied so future launches migrate normally.</item>
/// </list>
/// The point of case 3 is that upgrading no longer requires deleting the database and losing data.
/// </summary>
public sealed class AppInitializer(
    IWorkspacePaths paths,
    IServiceScopeFactory scopeFactory,
    ILogger<AppInitializer> logger) : IAppInitializer
{
    private readonly IWorkspacePaths _paths = paths;
    private readonly IServiceScopeFactory _scopeFactory = scopeFactory;
    private readonly ILogger<AppInitializer> _logger = logger;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var root = _paths.EnsureCreated();
        _logger.LogInformation(
            "Fenrix data root ready at {Root} (fallback: {Fallback})", root, _paths.UsingFallback);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var migrations = db.Database.GetMigrations().ToList();
        if (migrations.Count == 0)
        {
            // No migrations authored yet (dev bootstrap) → create the current model directly.
            await db.Database.EnsureCreatedAsync(cancellationToken);
            _logger.LogInformation(
                "Database ensured at {DbPath} (no EF migrations found; add one to switch to migrations).",
                _paths.DatabaseFilePath);
            return;
        }

        // Already under migration control → apply only genuinely pending migrations (incremental, no data loss).
        var applied = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
        if (applied.Any())
        {
            await db.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Database migrated to latest at {DbPath}", _paths.DatabaseFilePath);
            return;
        }

        // Nothing recorded as applied. If the database already has application tables, it was created by a
        // pre-migration EnsureCreated run: adopt it in place rather than failing on "table already exists".
        if (db.Database.IsSqlite() && await HasApplicationTablesAsync(db, cancellationToken))
        {
            _logger.LogWarning(
                "Existing database at {DbPath} has no migration history — adopting it into EF migrations (no data loss).",
                _paths.DatabaseFilePath);
            await AdoptExistingDatabaseAsync(db, cancellationToken);
            _logger.LogInformation("Database adopted into migrations at {DbPath}", _paths.DatabaseFilePath);
            return;
        }

        // Fresh/empty database → create the whole schema from migrations.
        await db.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Database created from migrations at {DbPath}", _paths.DatabaseFilePath);
    }

    /// <summary>
    /// Adopts a pre-migration database: creates any model tables/indexes that are missing on disk (e.g.
    /// tables added after the database was first created), then writes the migration history so EF treats
    /// the current migrations as already applied and only runs future ones.
    /// </summary>
    private async Task AdoptExistingDatabaseAsync(AppDbContext db, CancellationToken ct)
    {
        // 1) Reconcile the schema. GenerateCreateScript emits the full model DDL from the context's
        //    finalized model; we run each statement on its own and skip objects that already exist (from
        //    the earlier EnsureCreated), which leaves existing data untouched and creates only what's new.
        var createScript = db.Database.GenerateCreateScript();
        var created = 0;
        foreach (var statement in SplitStatements(createScript))
        {
            try
            {
                await db.Database.ExecuteSqlRawAsync(statement, ct);
                created++;
            }
            catch (DbException ex) when (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                // Object already present — leave it (and its data) as-is.
            }
        }
        _logger.LogInformation("Reconciled schema while adopting the database ({Created} new object(s)).", created);

        // 2) Record the migration history so subsequent launches migrate incrementally.
        var history = db.GetService<IHistoryRepository>();
        await db.Database.ExecuteSqlRawAsync(history.GetCreateIfNotExistsScript(), ct);

        var productVersion = ProductInfo.GetVersion();
        foreach (var migrationId in db.Database.GetMigrations())
            await db.Database.ExecuteSqlRawAsync(
                history.GetInsertScript(new HistoryRow(migrationId, productVersion)), ct);
    }

    /// <summary>Splits a generated DDL script into individual statements (the model has no literals containing ';').</summary>
    private static IEnumerable<string> SplitStatements(string script) =>
        script.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
              .Where(s => s.Length > 0);

    private static async Task<bool> HasApplicationTablesAsync(AppDbContext db, CancellationToken ct)
        => (await GetExistingTablesAsync(db, ct)).Count > 0;

    /// <summary>Reads the user tables that physically exist (SQLite), excluding internal and history tables.</summary>
    private static async Task<HashSet<string>> GetExistingTablesAsync(AppDbContext db, CancellationToken ct)
    {
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var connection = db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' " +
            "AND name NOT LIKE 'sqlite_%' AND name <> '__EFMigrationsHistory'";

        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            tables.Add(reader.GetString(0));

        return tables;
    }
}
