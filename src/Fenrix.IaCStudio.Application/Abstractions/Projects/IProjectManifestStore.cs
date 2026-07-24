using Fenrix.IaCStudio.Contracts.Projects;

namespace Fenrix.IaCStudio.Application.Abstractions.Projects;

/// <summary>
/// Reads and writes the non-secret <c>.fenrix/project-manifest.json</c> manifest. See docs/03-domain-model.md.
/// </summary>
public interface IProjectManifestStore
{
    /// <summary>True when a manifest exists for the project root.</summary>
    bool Exists(string projectRoot);

    /// <summary>Reads the manifest, or null when absent or unreadable.</summary>
    Task<ProjectManifest?> ReadAsync(string projectRoot, CancellationToken ct = default);

    /// <summary>Writes (creating <c>.fenrix</c> if needed) the manifest atomically.</summary>
    Task WriteAsync(string projectRoot, ProjectManifest manifest, CancellationToken ct = default);
}
