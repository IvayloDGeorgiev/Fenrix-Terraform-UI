using Fenrix.IaCStudio.Application.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Persistence;

/// <summary>
/// Creates the workspace tree, then ensures the database schema exists.
/// Phase 1 uses EnsureCreated; a later iteration switches to EF Core migrations
/// (see docs/12-database-design.md).
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
        await db.Database.EnsureCreatedAsync(cancellationToken);
        _logger.LogInformation("Database ready at {DbPath}", _paths.DatabaseFilePath);
    }
}
