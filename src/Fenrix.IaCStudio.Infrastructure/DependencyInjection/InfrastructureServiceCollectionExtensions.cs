using Fenrix.IaCStudio.Application.Abstractions;
using Fenrix.IaCStudio.Application.Abstractions.Authoring;
using Fenrix.IaCStudio.Application.Abstractions.Cloud;
using Fenrix.IaCStudio.Application.Abstractions.Connections;
using Fenrix.IaCStudio.Application.Abstractions.Editor;
using Fenrix.IaCStudio.Application.Abstractions.Deployments;
using Fenrix.IaCStudio.Application.Abstractions.Files;
using Fenrix.IaCStudio.Application.Abstractions.Git;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Providers;
using Fenrix.IaCStudio.Application.Abstractions.Security;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Infrastructure.Cloud;
using Fenrix.IaCStudio.Infrastructure.Connections;
using Fenrix.IaCStudio.Infrastructure.Deployments;
using Fenrix.IaCStudio.Infrastructure.Files;
using Fenrix.IaCStudio.Infrastructure.Git;
using Fenrix.IaCStudio.Infrastructure.Persistence;
using Fenrix.IaCStudio.Infrastructure.Processes;
using Fenrix.IaCStudio.Infrastructure.Projects;
using Fenrix.IaCStudio.Infrastructure.Providers;
using Fenrix.IaCStudio.Infrastructure.Security;
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

        // State & inspection tools (Phase 9). Inspection is read-only (no lock, JSON output never logged);
        // the state-ops and import services are state-changing (confirm + per-environment lock + the Phase 8
        // authentication-required rule + redacted history). All touch the DB / process runner and are scoped.
        // No new services beyond these three; no new DB migration. See docs/05, docs/22, docs/25.
        services.AddScoped<ITerraformInspectionService, TerraformInspectionService>();
        services.AddScoped<ITerraformStateService, TerraformStateService>();
        services.AddScoped<ITerraformImportService, TerraformImportService>();

        // Git engine (Phase 5). Discovery reads settings; the coordinator records redacted history and
        // logs to Logs/git; the service drives the git CLI via the shared ArgumentList runner. All are
        // scoped to the UI request. See docs/08-git-engine.md, docs/23-command-transparency.md.
        services.AddScoped<IGitDiscovery, GitDiscovery>();
        services.AddScoped<GitProcessCoordinator>();
        services.AddScoped<IGitRepositoryInitializer, GitRepositoryInitializer>();
        services.AddScoped<IGitService, GitService>();

        // Provider integrations, secrets & the Connections library (Phase 7). Secret backends are stateless
        // singletons; the secret-store facade dispatches by provider. Repository-host adapters are registered
        // as IRepositoryProvider and resolved by the (scoped) factory, which reads tokens from the store
        // just-in-time. The connection service touches the DB and is scoped. Host adapters use typed
        // HttpClients from the factory. See docs/09-provider-integrations.md, docs/11-secrets.md, docs/26-connections.md.
        services.AddHttpClient();
        services.AddSingleton<WindowsCredentialManagerStore>();
        services.AddSingleton<ISecretStore, SecretStore>();

        services.AddSingleton<IRepositoryProvider, GenericGitProvider>();
        services.AddSingleton<IRepositoryProvider, GitHubProvider>();
        services.AddSingleton<IRepositoryProvider, AzureDevOpsProvider>();
        services.AddSingleton<IRepositoryProvider, BitbucketProvider>();
        services.AddSingleton<IRepositoryProvider, GitLabProvider>();
        services.AddSingleton<IRepositoryProvider, AwsCodeCommitProvider>();

        services.AddScoped<IRepositoryProviderFactory, RepositoryProviderFactory>();
        services.AddScoped<IConnectionService, ConnectionService>();
        services.AddScoped<IRepositoryHostService, RepositoryHostService>();

        // Cloud connections (Phase 8). Adapters drive the official CLIs (az/aws/gcloud) via the shared
        // process runner and are stateless singletons. The factory resolves an adapter + any service-principal
        // secret just-in-time; the composer bridges a bound connection into Terraform execution — both touch
        // the DB / secret store and are scoped. Fenrix stores only a SecretReference, never a credential value.
        // See docs/10-cloud-integrations.md, docs/11-secrets.md, docs/26-connections.md.
        services.AddSingleton<ICloudConnectionProvider, AzureCloudProvider>();
        services.AddSingleton<ICloudConnectionProvider, AwsCloudProvider>();
        services.AddSingleton<ICloudConnectionProvider, GoogleCloudProvider>();
        services.AddScoped<ICloudConnectionProviderFactory, CloudConnectionProviderFactory>();
        services.AddScoped<ICloudEnvironmentComposer, CloudEnvironmentComposer>();

        // Project secrets & key-pair management (Phase 8.5). Private keys are encrypted at rest with DPAPI
        // (stateless singleton protector) and placed in the secure per-project keys folder by KeyStore; the
        // generation runner drives Terraform (tls_private_key [+ aws_key_pair]) through the shared coordinator.
        // The key service touches the DB / secret store and is scoped. Fenrix stores only metadata + a
        // SecretReference — never the private bytes. See docs/28-key-pair-management.md, docs/11-secrets.md.
        services.AddSingleton<IKeyProtector, DpapiKeyProtector>();
        services.AddScoped<KeyStore>();
        services.AddScoped<KeyGenerationRunner>();
        services.AddScoped<IKeyPairService, KeyPairService>();

        // CI/CD pipelines & deployments (Phase 9.5). Version + pipeline services are plain DB CRUD; the
        // recorder is the single writer of Deployment history (invoked inside the apply service after a
        // successful apply, so every apply lands on the board); the deployment service orchestrates the
        // governed deploy (plan → gates → apply) + board/matrix/fan-out over the Phase 4 spine. All scoped.
        // No bypass of the saved-plan-only apply rule. See docs/20-pipelines-deployments.md.
        services.AddScoped<IProjectVersionService, ProjectVersionService>();
        services.AddScoped<IDeploymentRecorder, DeploymentRecorder>();
        services.AddScoped<IPipelineService, PipelineService>();
        services.AddScoped<IDeploymentService, DeploymentService>();

        // Visual resource builder (Phase 10). The schema service captures & caches provider schemas
        // (read-only providers schema -json, offline cache under Cache/terraform-schemas); the authoring
        // service writes schema-driven / form-authored HCL to real .tf files through the atomic-write +
        // file-history path and splices spans for literal round-trip edits. Both touch the process runner /
        // filesystem and are scoped. No new DB migration (files are the source of truth).
        // See docs/07-visual-builder.md, docs/22-terraform-files-model.md.
        services.AddScoped<IProviderSchemaService, ProviderSchemaService>();
        services.AddScoped<IConfigAuthoringService, ConfigAuthoringService>();

        // Terraform-aware code editor (Phase 10.5). The format service runs `fmt -` over the editor buffer
        // (stdin → stdout) through the shared executor + coordinator (captureLog:false, redacted history);
        // outline, snippets, and reference helpers are pure Application logic invoked directly from the UI.
        // No new DB migration. See docs/05-terraform-engine.md, docs/13-ui-design.md.
        services.AddScoped<IEditorFormatService, EditorFormatService>();

        return services;
    }
}
