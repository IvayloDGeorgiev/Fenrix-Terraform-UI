using Fenrix.IaCStudio.Contracts.Projects;

namespace Fenrix.IaCStudio.Application.Abstractions.Projects;

/// <summary>
/// Creates the recommended on-disk structure for a new project (folders, starter files,
/// .gitignore, README). See docs/03-domain-model.md.
/// </summary>
public interface IProjectScaffolder
{
    /// <summary>
    /// Creates the project folder tree under <paramref name="projectRoot"/> for the given request.
    /// The directory must not already exist as a non-empty folder.
    /// </summary>
    Task ScaffoldAsync(string projectRoot, CreateProjectRequest request, CancellationToken ct = default);
}
