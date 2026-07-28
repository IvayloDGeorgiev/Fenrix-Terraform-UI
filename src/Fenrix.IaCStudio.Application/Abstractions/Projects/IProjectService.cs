using Fenrix.IaCStudio.Contracts.Projects;
using Fenrix.IaCStudio.Domain.Projects;

namespace Fenrix.IaCStudio.Application.Abstractions.Projects;

/// <summary>
/// Registers and retrieves projects. Files on disk remain the source of truth; this manages the
/// logical registration/index plus scaffolding and manifest side effects. See docs/03-domain-model.md.
/// </summary>
public interface IProjectService
{
    /// <summary>Creates a new project on disk with the recommended structure and registers it.</summary>
    Task<InfrastructureProject> CreateAsync(CreateProjectRequest request, CancellationToken ct = default);

    /// <summary>Registers an existing folder in place (never moves or rewrites files) from a scan + mappings.</summary>
    Task<InfrastructureProject> ImportAsync(ImportScanResult scan, CancellationToken ct = default);

    /// <summary>All registered projects (optionally including archived), newest activity first.</summary>
    Task<IReadOnlyList<ProjectSummary>> ListAsync(bool includeArchived = false, CancellationToken ct = default);

    /// <summary>The most recently opened projects, most recent first.</summary>
    Task<IReadOnlyList<ProjectSummary>> GetRecentAsync(int take = 8, CancellationToken ct = default);

    /// <summary>Loads a single project with its environments, or null if not found.</summary>
    Task<InfrastructureProject?> GetAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Marks a project as opened now (updates last-opened for the recent list).</summary>
    Task TouchAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Archives or unarchives a project (kept for history; hidden from the default list).</summary>
    Task SetArchivedAsync(Guid projectId, bool archived, CancellationToken ct = default);

    /// <summary>Binds (or clears, when null) the repository connection a project maps to. See docs/26-connections.md.</summary>
    Task SetRepositoryConnectionAsync(Guid projectId, Guid? repositoryConnectionId, CancellationToken ct = default);

    /// <summary>
    /// Binds (or clears, when null) the cloud connection an environment authenticates with. The cloud
    /// connection is bound per environment, never on the project. See docs/26-connections.md.
    /// </summary>
    Task SetEnvironmentCloudConnectionAsync(
        Guid projectId, Guid environmentId, Guid? cloudConnectionId, CancellationToken ct = default);

    /// <summary>
    /// Records (or clears, when null) the Terraform workspace an environment is bound to, after a successful
    /// <c>workspace select</c>/<c>new</c>. Persists to <see cref="Domain.Environments.ProjectEnvironment.TerraformWorkspace"/>.
    /// See docs/05-terraform-engine.md.
    /// </summary>
    Task SetEnvironmentWorkspaceAsync(
        Guid projectId, Guid environmentId, string? workspace, CancellationToken ct = default);

    /// <summary>Unregisters a project from Fenrix. Never deletes files on disk.</summary>
    Task RemoveAsync(Guid projectId, CancellationToken ct = default);
}
