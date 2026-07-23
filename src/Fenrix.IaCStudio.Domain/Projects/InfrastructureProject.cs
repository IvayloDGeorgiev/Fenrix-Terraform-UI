using Fenrix.IaCStudio.Domain.Environments;

namespace Fenrix.IaCStudio.Domain.Projects;

/// <summary>
/// A registered Terraform project. Files on disk remain the source of truth;
/// this is the logical registration/index. See docs/03-domain-model.md.
/// </summary>
public sealed class InfrastructureProject
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    /// <summary>The project folder on disk.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>The Git repository root, if different from <see cref="RootPath"/>.</summary>
    public string? RepositoryRootPath { get; set; }

    public string? Description { get; set; }
    public string? RequiredTerraformVersion { get; set; }

    /// <summary>Optional client/customer this project belongs to.</summary>
    public Guid? ClientId { get; set; }

    /// <summary>The single repository connection for this project (a project maps to one repo).</summary>
    public Guid? RepositoryConnectionId { get; set; }

    public bool IsArchived { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastOpenedAt { get; set; }

    public ICollection<ProjectEnvironment> Environments { get; set; } = [];
}
