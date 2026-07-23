using Fenrix.IaCStudio.Domain.Cloud;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Execution;
using Fenrix.IaCStudio.Domain.Projects;
using Fenrix.IaCStudio.Domain.Security;
using Fenrix.IaCStudio.Domain.Settings;
using Fenrix.IaCStudio.Domain.Versioning;
using Microsoft.EntityFrameworkCore;

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
    public DbSet<ProjectVersion> ProjectVersions => Set<ProjectVersion>();
    public DbSet<Deployment> Deployments => Set<Deployment>();
    public DbSet<CommandRun> CommandRuns => Set<CommandRun>();
    public DbSet<RecentFile> RecentFiles => Set<RecentFile>();
    public DbSet<SettingEntry> Settings => Set<SettingEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
