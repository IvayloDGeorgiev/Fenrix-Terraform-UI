using Fenrix.IaCStudio.Domain.Enterprise;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fenrix.IaCStudio.Infrastructure.Persistence;

// Phase 11 enterprise metadata. Enum discriminators are stored as strings to keep the tables
// human-readable (matching CommandRun.Status / SavedPlan.Mode); the Permission [Flags] enum stays an
// int so bitwise checks translate. Provider-agnostic (no SQLite-only column types). See docs/29-enterprise.md.

internal sealed class OrgUserConfiguration : IEntityTypeConfiguration<OrgUser>
{
    public void Configure(EntityTypeBuilder<OrgUser> b)
    {
        b.ToTable("OrgUsers");
        b.HasKey(x => x.Id);
        b.Property(x => x.UserKey).IsRequired().HasMaxLength(256);
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(256);
        b.Property(x => x.Email).HasMaxLength(320);
        // UserKey is the stable identity referenced by assignments/audit; unique per store.
        b.HasIndex(x => x.UserKey).IsUnique();
    }
}

internal sealed class OrgRoleConfiguration : IEntityTypeConfiguration<OrgRole>
{
    public void Configure(EntityTypeBuilder<OrgRole> b)
    {
        b.ToTable("OrgRoles");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.Property(x => x.Description).HasMaxLength(400);
        // Permissions is a [Flags] enum — persist as its int value so bitwise unions/checks work.
        b.Property(x => x.Permissions).HasConversion<int>();
        b.HasIndex(x => x.Name).IsUnique();
    }
}

internal sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> b)
    {
        b.ToTable("RoleAssignments");
        b.HasKey(x => x.Id);
        b.Property(x => x.UserKey).IsRequired().HasMaxLength(256);
        b.Property(x => x.Scope).HasConversion<string>().HasMaxLength(20);
        // Authorisation resolves by user; project/environment narrow the scope.
        b.HasIndex(x => x.UserKey);
        b.HasIndex(x => new { x.UserKey, x.ProjectId });
        b.HasIndex(x => new { x.UserKey, x.EnvironmentId });
    }
}

internal sealed class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> b)
    {
        b.ToTable("AuditEvents");
        b.HasKey(x => x.Id);
        b.Property(x => x.Action).HasConversion<string>().HasMaxLength(40);
        b.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.UserKey).IsRequired().HasMaxLength(256);
        b.Property(x => x.UserDisplayName).HasMaxLength(256);
        b.Property(x => x.ProjectName).HasMaxLength(200);
        b.Property(x => x.EnvironmentName).HasMaxLength(120);
        b.Property(x => x.Target).HasMaxLength(1024);
        b.Property(x => x.Detail).HasMaxLength(2048);
        // Browsed newest-first, filtered by user / project / action.
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => new { x.ProjectId, x.OccurredAt });
        b.HasIndex(x => x.UserKey);
        b.HasIndex(x => x.Action);
    }
}

internal sealed class OrgPolicyConfiguration : IEntityTypeConfiguration<OrgPolicy>
{
    public void Configure(EntityTypeBuilder<OrgPolicy> b)
    {
        b.ToTable("OrgPolicies");
        b.HasKey(x => x.Id);
        b.Property(x => x.RequiredBranchForProduction).HasMaxLength(200);
        b.Property(x => x.AllowedTerraformVersionConstraint).HasMaxLength(200);
        b.Property(x => x.UpdatedBy).HasMaxLength(256);
        b.Property(x => x.RequireApprovalForEnvironments)
            .HasConversion(ConfigHelpers.StringListConverter)
            .Metadata.SetValueComparer(ConfigHelpers.StringListComparer);
    }
}

internal sealed class ConfigTemplateConfiguration : IEntityTypeConfiguration<ConfigTemplate>
{
    public void Configure(EntityTypeBuilder<ConfigTemplate> b)
    {
        b.ToTable("ConfigTemplates");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.Category).HasMaxLength(120);
        b.Property(x => x.DefaultTargetFile).HasMaxLength(1024);
        b.Property(x => x.CreatedBy).HasMaxLength(256);
        b.HasIndex(x => x.Name);
        b.HasIndex(x => x.Category);
        b.HasMany(x => x.Parameters)
            .WithOne()
            .HasForeignKey(p => p.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TemplateParameterConfiguration : IEntityTypeConfiguration<TemplateParameter>
{
    public void Configure(EntityTypeBuilder<TemplateParameter> b)
    {
        b.ToTable("TemplateParameters");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).IsRequired().HasMaxLength(120);
        b.Property(x => x.Description).HasMaxLength(1000);
        b.Property(x => x.DefaultValue).HasMaxLength(2048);
        b.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        b.HasIndex(x => x.TemplateId);
    }
}

internal sealed class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> b)
    {
        b.ToTable("ApprovalRequests");
        b.HasKey(x => x.Id);
        b.Property(x => x.EnvironmentName).HasMaxLength(120);
        b.Property(x => x.VersionLabel).HasMaxLength(100);
        b.Property(x => x.GitCommit).HasMaxLength(64);
        b.Property(x => x.PlanFileHash).HasMaxLength(64);
        b.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        b.Property(x => x.RequestedByKey).HasMaxLength(256);
        b.Property(x => x.RequestedByName).HasMaxLength(256);
        b.Property(x => x.RequestNote).HasMaxLength(1000);
        b.Property(x => x.DecidedByKey).HasMaxLength(256);
        b.Property(x => x.DecidedByName).HasMaxLength(256);
        b.Property(x => x.DecisionNote).HasMaxLength(1000);
        // The inbox lists pending requests; a deploy looks up the latest for its env/plan.
        b.HasIndex(x => new { x.Status, x.RequestedAt });
        b.HasIndex(x => new { x.ProjectId, x.EnvironmentId, x.Status });
        b.HasIndex(x => x.SavedPlanId);
    }
}
