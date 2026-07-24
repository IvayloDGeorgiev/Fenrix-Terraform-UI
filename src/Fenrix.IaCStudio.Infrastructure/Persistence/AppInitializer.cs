using Fenrix.IaCStudio.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Persistence;

/// <summary>
/// Creates the workspace tree, then brings the database schema up to date. Uses EF Core migrations
/// when any are present in the assembly, and falls back to <c>EnsureCreated</c> until the initial
/// migration has been generated. See docs/12-database-design.md.
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

        // Prefer migrations once they exist; fall back to EnsureCreated beforehand so the app still
        // runs while the initial migration is being introduced (see docs/12-database-design.md).
        if (db.Database.GetMigrations().Any())
        {
            await db.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Database migrated to latest at {DbPath}", _paths.DatabaseFilePath);
        }
        else
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            _logger.LogInformation(
                "Database ensured at {DbPath} (no EF migrations found; run 'dotnet ef migrations add InitialCreate' to switch)",
                _paths.DatabaseFilePath);
        }
    }
}
