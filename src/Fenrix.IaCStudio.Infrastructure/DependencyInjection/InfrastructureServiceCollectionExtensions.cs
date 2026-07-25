using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Application.Abstractions.Git;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Infrastructure.Files;
using Fenrix.IaCStudio.Infrastructure.Git;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Fenrix.IaCStudio.Infrastructure.Processes;
using Fenrix.IaCStudio.Infrastructure.Projects;
using Fenrix.IaCStudio.Infrastructure.Terraform;
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

        // Projects (Phase 2). Stateless helpers are singletons; DB-touching services are scoped.
        services.AddSingleton<IProjectScaffolder, ProjectScaffolder>();
        services.AddSingleton<IProjectManifestStore, ProjectManifestStore>();
        services.AddSingleton<IProjectImportScanner, ProjectImportScanner>();
        services.AddScoped<IProjectService, ProjectService>();

        // Files, history & recovery (Phase 2). The journal must be shared so the synchronizer
        // recognises the tree service's own writes (loop prevention).
        services.AddSingleton<IChangeJournal, ChangeJournal>();
        services.AddSingleton<IRecycleBin, RecycleBin>();
        services.AddSingleton<IProjectFileSynchronizer, ProjectFileSynchronizer>();
        services.AddScoped<IFileHistoryStore, FileHistoryStore>();
        services.AddScoped<IFileTreeService, FileTreeService>();

        // Terraform execution foundation (Phase 3). The process runner is stateless (singleton);
        // discovery/history/executor touch settings or the DB and are resolved per UI scope.
        // See docs/05-terraform-engine.md, docs/23-command-transparency.md, docs/25-execution-lifecycle.md.
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddScoped<ITerraformDiscovery, TerraformDiscovery>();
        services.AddScoped<ICommandHistoryStore, EfCommandHistoryStore>();
        services.AddScoped<ITerraformExecutor, TerraformExecutor>();

        // Plans & deployment safety (Phase 4). The lock service is a singleton (its in-process guard is
        // shared across all operations); plan/apply services and the saved-plan store touch the DB and are
        // scoped. The process coordinator centralizes redacted history + logging for the new services.
        // See docs/06-plan-apply-safety.md, docs/25-execution-lifecycle.md.
        services.AddSingleton<IEnvironmentLockService, FileEnvironmentLockService>();
        services.AddScoped<TerraformProcessCoordinator>();
        services.AddScoped<ISavedPlanStore, EfSavedPlanStore>();
        services.AddScoped<ITerraformPlanService, TerraformPlanService>();
        services.AddScoped<ITerraformApplyService, TerraformApplyService>();

        // Git engine (Phase 5). Discovery reads settings; the coordinator records redacted history and
        // logs to Logs/git; the service drives the git CLI via the shared ArgumentList runner. All are
        // scoped to the UI request. See docs/08-git-engine.md, docs/23-command-transparency.md.
        services.AddScoped<IGitDiscovery, GitDiscovery>();
        services.AddScoped<GitProcessCoordinator>();
        services.AddScoped<IGitRepositoryInitializer, GitRepositoryInitializer>();
        services.AddScoped<IGitService, GitService>();

        return services;
    }
}
