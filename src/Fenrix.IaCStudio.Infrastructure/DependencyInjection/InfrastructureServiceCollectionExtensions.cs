using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Fenrix.IaCStudio.Infrastructure.Workspace;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.DependencyInjection;

/// <summary>
/// Registers Infrastructure implementations: workspace paths, EF Core (SQLite),
/// settings store, and the startup initializer. See docs/01-architecture.md.
/// </summary>
public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddFenrixInfrastructure(
        this IServiceCollection services, string? dataRootOverride = null)
    {
        services.AddSingleton<IWorkspacePaths>(sp =>
            new WorkspacePaths(sp.GetRequiredService<ILogger<WorkspacePaths>>(), dataRootOverride));

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var paths = sp.GetRequiredService<IWorkspacePaths>();
            options.UseSqlite($"Data Source={paths.DatabaseFilePath}");
        });

        services.AddScoped<ISettingsStore, EfSettingsStore>();
        services.AddSingleton<IAppInitializer, AppInitializer>();

        return services;
    }
}
