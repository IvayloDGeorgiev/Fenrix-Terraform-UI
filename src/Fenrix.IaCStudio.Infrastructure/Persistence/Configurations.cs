using System.Text.Json;
using Fenrix.IaCStudio.Domain.Cloud;
using Fenrix.IaCStudio.Domain.Environments;
using Fenrix.IaCStudio.Domain.Execution;
using Fenrix.IaCStudio.Domain.Files;
using Fenrix.IaCStudio.Domain.Projects;
using Fenrix.IaCStudio.Domain.Settings;
using Fenrix.IaCStudio.Domain.Terraform;
using Fenrix.IaCStudio.Domain.Versioning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Fenrix.IaCStudio.Infrastructure.Persistence;

/// <summary>Shared helpers for entity configuration.</summary>
internal static class ConfigHelpers
{
    /// <summary>Stores a List&lt;string&gt; as a JSON column, with a value comparer for change tracking.</summary>
    public static readonly ValueConverter<List<string>, string> StringListConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => string.IsNullOrEmpty(v)
            ? new List<string>()
            : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

    public static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
        v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
        v => v == null ? new List<string>() : v.ToList());
}

internal sealed class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> b)
    {
        b.ToTable("Clients");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.Name);
        b.Property(x => x.Tags)
            .HasConversion(ConfigHelpers.StringListConverter)
            .Metadata.SetValueComparer(ConfigHelpers.StringListComparer);
    }
}

internal sealed class ProjectConfiguration : IEntityTypeConfiguration<InfrastructureProject>
{
    public void Configure(EntityTypeBuilder<InfrastructureProject> b)
    {
        b.ToTable("Projects");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.RootPath).IsRequired();
        b.HasIndex(x => x.Name);
        b.HasIndex(x => x.ClientId);
        b.HasMany(x => x.Environments)
            .WithOne()
            .HasForeignKey(e => e.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class EnvironmentConfiguration : IEntityTypeConfiguration<ProjectEnvironment>
{
    public void Configure(EntityTypeBuilder<ProjectEnvironment> b)
    {
        b.ToTable("Environments");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.HasIndex(x => x.ProjectId);
        b.HasIndex(x => x.CloudConnectionId);
    }
}

internal sealed class CloudConnectionConfiguration : IEntityTypeConfiguration<CloudConnection>
{
    public void Configure(EntityTypeBuilder<CloudConnection> b)
    {
        b.ToTable("CloudConnections");
        b.HasKey(x => x.Id);
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        // hot columns for search at scale (hundreds–thousands of connections)
        b.HasIndex(x => x.ClientId);
        b.HasIndex(x => x.ProviderType);
        b.HasIndex(x => x.DisplayName);
        b.Property(x => x.Tags)
            .HasConversion(ConfigHelpers.StringListConverter)
            .Metadata.SetValueComparer(ConfigHelpers.StringListComparer);
    }
}

internal sealed class RepositoryConnectionConfiguration : IEntityTypeConfiguration<RepositoryConnection>
{
    public void Configure(EntityTypeBuilder<RepositoryConnection> b)
    {
        b.ToTable("RepositoryConnections");
        b.HasKey(x => x.Id);
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(200);
        // hot columns for search at scale, matching CloudConnection
        b.HasIndex(x => x.ClientId);
        b.HasIndex(x => x.ProviderType);
        b.HasIndex(x => x.DisplayName);
        b.Property(x => x.Tags)
            .HasConversion(ConfigHelpers.StringListConverter)
            .Metadata.SetValueComparer(ConfigHelpers.StringListComparer);
    }
}

internal sealed class ProjectVersionConfiguration : IEntityTypeConfiguration<ProjectVersion>
{
    public void Configure(EntityTypeBuilder<ProjectVersion> b)
    {
        b.ToTable("ProjectVersions");
        b.HasKey(x => x.Id);
        b.Property(x => x.Label).IsRequired().HasMaxLength(100);
        b.HasIndex(x => x.ProjectId);
    }
}

internal sealed class DeploymentConfiguration : IEntityTypeConfiguration<Deployment>
{
    public void Configure(EntityTypeBuilder<Deployment> b)
    {
        b.ToTable("Deployments");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.EnvironmentId, x.Status });
        b.HasIndex(x => x.ProjectId);
        b.HasIndex(x => x.ProjectVersionId);
    }
}

internal sealed class RecentFileConfiguration : IEntityTypeConfiguration<RecentFile>
{
    public void Configure(EntityTypeBuilder<RecentFile> b)
    {
        b.ToTable("RecentFiles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Path).IsRequired();
        b.HasIndex(x => x.ProjectId);
        b.HasIndex(x => x.LastOpenedAt);
    }
}

internal sealed class FileIdentityConfiguration : IEntityTypeConfiguration<FileIdentity>
{
    public void Configure(EntityTypeBuilder<FileIdentity> b)
    {
        b.ToTable("FileIdentities");
        b.HasKey(x => x.Id);
        b.Property(x => x.CurrentRelativePath).IsRequired().HasMaxLength(1024);
        // one identity per (project, current path); renames update the path in place
        b.HasIndex(x => new { x.ProjectId, x.CurrentRelativePath });
        b.HasIndex(x => new { x.ProjectId, x.IsDeleted });
    }
}

internal sealed class FileVersionConfiguration : IEntityTypeConfiguration<FileVersion>
{
    public void Configure(EntityTypeBuilder<FileVersion> b)
    {
        b.ToTable("FileVersions");
        b.HasKey(x => x.Id);
        b.Property(x => x.RelativePath).IsRequired().HasMaxLength(1024);
        b.Property(x => x.ContentHash).HasMaxLength(64);
        b.HasIndex(x => new { x.FileIdentityId, x.CapturedAt });
        b.HasIndex(x => x.ProjectId);
        b.HasIndex(x => x.BlobId);
    }
}

internal sealed class FileBlobConfiguration : IEntityTypeConfiguration<FileBlob>
{
    public void Configure(EntityTypeBuilder<FileBlob> b)
    {
        b.ToTable("FileBlobs");
        b.HasKey(x => x.Id);
        b.Property(x => x.ContentHash).IsRequired().HasMaxLength(64);
        b.HasIndex(x => x.ContentHash).IsUnique(); // the dedup key
    }
}

internal sealed class CommandRunConfiguration : IEntityTypeConfiguration<CommandRun>
{
    public void Configure(EntityTypeBuilder<CommandRun> b)
    {
        b.ToTable("CommandRuns");
        b.HasKey(x => x.Id);
        b.Property(x => x.Tool).IsRequired().HasMaxLength(40);
        b.Property(x => x.Command).IsRequired().HasMaxLength(120);
        b.Property(x => x.Status).IsRequired().HasMaxLength(40);
        // History is browsed newest-first, scoped by project/environment.
        b.HasIndex(x => x.StartedAt);
        b.HasIndex(x => new { x.ProjectId, x.StartedAt });
        b.HasIndex(x => x.EnvironmentId);
    }
}

internal sealed class SavedPlanConfiguration : IEntityTypeConfiguration<SavedPlan>
{
    public void Configure(EntityTypeBuilder<SavedPlan> b)
    {
        b.ToTable("SavedPlans");
        b.HasKey(x => x.Id);
        b.Property(x => x.EnvironmentName).HasMaxLength(120);
        b.Property(x => x.PlanFilePath).IsRequired().HasMaxLength(1024);
        b.Property(x => x.RelativePlanFilePath).HasMaxLength(1024);
        b.Property(x => x.WorkingDirectory).HasMaxLength(1024);
        b.Property(x => x.TerraformVersion).HasMaxLength(40);
        // Integrity hashes are lowercase hex SHA-256 (64 chars).
        b.Property(x => x.ConfigHash).HasMaxLength(64);
        b.Property(x => x.LockHash).HasMaxLength(64);
        b.Property(x => x.PlanFileHash).HasMaxLength(64);
        b.Property(x => x.GitCommitSha).HasMaxLength(64);
        b.Property(x => x.GitBranch).HasMaxLength(200);
        b.Property(x => x.InvalidatedReason).HasMaxLength(400);
        // Stored as its enum name so the table stays human-readable (as with CommandRun.Status).
        b.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
        // Browsed newest-first, scoped by project and environment.
        b.HasIndex(x => new { x.ProjectId, x.CreatedAt });
        b.HasIndex(x => new { x.EnvironmentId, x.CreatedAt });
    }
}

internal sealed class SettingConfiguration : IEntityTypeConfiguration<SettingEntry>
{
    public void Configure(EntityTypeBuilder<SettingEntry> b)
    {
        b.ToTable("Settings");
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).IsRequired().HasMaxLength(200);
        // one value per (key, scope, scopeId)
        b.HasIndex(x => new { x.Key, x.Scope, x.ScopeId }).IsUnique();
    }
}
