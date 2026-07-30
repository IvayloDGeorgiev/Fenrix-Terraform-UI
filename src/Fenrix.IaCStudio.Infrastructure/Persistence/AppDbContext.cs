using Fenrix.IaCStudio.Domain.Cloud;
using Fenrix.IaCStudio.Domain.Enterprise;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Execution;
using Fenrix.IaCStudio.Domain.Files;
using Fenrix.IaCStudio.Domain.Projects;
using Fenrix.IaCStudio.Domain.Security;
using Fenrix.IaCStudio.Domain.Settings;
using Fenrix.IaCStudio.Domain.Terraform;
using Fenrix.IaCStudio.Domain.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fenrix.IaCStudio.Infrastructure.Persistence;

/// <summary>
/// The Fenrix index/cache database. Files on disk remain the source of truth;
/// this stores registrations, mappings, history and settings. Provider-agnostic
/// (SQLite by default, SQL Server optional). See docs/12-database-design.md.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<InfrastructureProject> Projects => Set<InfrastructureProject>();
    public DbSet<ProjectEnvironment> Environments => Set<ProjectEnvironment>();
    public DbSet<CloudConnection> CloudConnections => Set<CloudConnection>();
    public DbSet<RepositoryConnection> RepositoryConnections => Set<RepositoryConnection>();
    public DbSet<SecretReference> SecretReferences => Set<SecretReference>();
    public DbSet<KeyPair> KeyPairs => Set<KeyPair>();
    public DbSet<ProjectVersion> ProjectVersions => Set<ProjectVersion>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<DeploymentPipeline> DeploymentPipelines => Set<DeploymentPipeline>();
    public DbSet<PipelineStage> PipelineStages => Set<PipelineStage>();
    public DbSet<CommandRun> CommandRuns => Set<CommandRun>();
    public DbSet<SavedPlan> SavedPlans => Set<SavedPlan>();
    public DbSet<RecentFile> RecentFiles => Set<RecentFile>();
    public DbSet<SettingEntry> Settings => Set<SettingEntry>();

    // Enterprise capability (Phase 11). Shared metadata: identity, roles, audit, policy, templates,
    // approvals. Persisted to SQLite or SQL Server (same model). See docs/29-enterprise.md.
    public DbSet<OrgUser> OrgUsers => Set<OrgUser>();
    public DbSet<OrgRole> OrgRoles => Set<OrgRole>();
    public DbSet<RoleAssignment> RoleAssignments => Set<RoleAssignment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<OrgPolicy> OrgPolicies => Set<OrgPolicy>();
    public DbSet<ConfigTemplate> ConfigTemplates => Set<ConfigTemplate>();
    public DbSet<TemplateParameter> TemplateParameters => Set<TemplateParameter>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    // File version history & recovery (Phase 2). See docs/21-file-history-recovery.md.
    public DbSet<FileIdentity> FileIdentities => Set<FileIdentity>();
    public DbSet<FileVersion> FileVersions => Set<FileVersion>();
    public DbSet<FileBlob> FileBlobs => Set<FileBlob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SQLite has no native DateTimeOffset type and cannot ORDER BY / compare it in SQL.
        // Store all DateTimeOffset values as a sortable binary long so queries translate.
        // SQL Server has a native type, so this converter is only applied for SQLite.
        if (Database.IsSqlite())
        {
            configurationBuilder.Properties<DateTimeOffset>()
                .HaveConversion<DateTimeOffsetToBinaryConverter>();
        }

        base.ConfigureConventions(configurationBuilder);
    }
}
