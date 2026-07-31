using Fenrix.IaCStudio.Contracts.Projects;

namespace Fenrix.IaCStudio.Application.Abstractions.Projects;

/// <summary>
/// The project-template catalog (Phase 12). Templates are complete, cost-aware Terraform starters selectable at
/// project creation: choosing one prefills every environment's working directory with real, ready-to-edit
/// configuration (networking, security, compute/storage — everything that type of project needs). Built-in
/// templates ship with the app; user templates live as files under <c>&lt;dataRoot&gt;\Templates</c>. Nothing here
/// touches the database. See docs/32-project-templates.md.
/// </summary>
public interface IProjectTemplateService
{
    /// <summary>All templates (built-in + user), for the gallery.</summary>
    IReadOnlyList<ProjectTemplateInfo> List();

    /// <summary>A full template (metadata + files) by id, or null if unknown.</summary>
    ProjectTemplate? Get(string id);

    /// <summary>
    /// Writes a template's files into each environment's working directory under <paramref name="projectRoot"/>.
    /// A file named <c>terraform.tfvars</c> is written as the environment's own <c>&lt;slug&gt;.tfvars</c>. Called
    /// right after scaffolding a new project; overwrites the placeholder starter files.
    /// </summary>
    Task ApplyAsync(string templateId, string projectRoot, IEnumerable<string> environmentSlugs, CancellationToken ct = default);

    /// <summary>Creates or updates a user template (create when <see cref="SaveTemplateRequest.Id"/> is null).</summary>
    Task<ProjectTemplateInfo> SaveUserTemplateAsync(SaveTemplateRequest request, CancellationToken ct = default);

    /// <summary>Deletes a user template. Built-in templates cannot be deleted.</summary>
    Task DeleteUserTemplateAsync(string id, CancellationToken ct = default);

    /// <summary>
    /// Builds a template from an existing project's first environment working directory (its <c>.tf</c>/<c>.tfvars</c>
    /// files) and saves it as a user template. A quick way to turn a working project into a reusable starter.
    /// </summary>
    Task<ProjectTemplateInfo> CreateFromProjectAsync(
        string projectRootPath, string environmentWorkingDir, SaveTemplateRequest metadata, CancellationToken ct = default);
}
