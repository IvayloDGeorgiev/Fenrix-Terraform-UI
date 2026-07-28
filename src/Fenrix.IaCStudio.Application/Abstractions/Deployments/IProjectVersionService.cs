using Fenrix.IaCStudio.Contracts.Deployments;

namespace Fenrix.IaCStudio.Application.Abstractions.Deployments;

/// <summary>
/// Manages a project's versions — per-project, Git-anchored candidates that can each be deployed to any/all
/// environments independently. A version is a Git ref plus metadata (nothing is copied): "cut a version"
/// snapshots the current HEAD (config + provider-lock hashes) and optionally pushes an annotated tag; versions
/// can also be inferred from tags already in the repository. See docs/20-pipelines-deployments.md.
/// </summary>
public interface IProjectVersionService
{
    /// <summary>All versions for a project, newest first.</summary>
    Task<IReadOnlyList<ProjectVersionSummary>> ListAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Loads a single version, or null.</summary>
    Task<ProjectVersionSummary?> GetAsync(Guid versionId, CancellationToken ct = default);

    /// <summary>
    /// Cuts a new version from the project's current Git HEAD: captures the commit, branch, and the config /
    /// provider-lock hashes, and (optionally) creates + pushes an annotated tag named after the label.
    /// </summary>
    Task<CutVersionResult> CutAsync(CutVersionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Imports versions from existing Git tags that don't yet have a <see cref="Fenrix.IaCStudio.Domain.Versioning.ProjectVersion"/>
    /// row (semver-looking tags are labelled from the tag). Returns the versions created.
    /// </summary>
    Task<IReadOnlyList<ProjectVersionSummary>> InferFromTagsAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Updates a version's editable metadata (label, notes). Never changes the Git anchor.</summary>
    Task<ProjectVersionSummary?> UpdateAsync(Guid versionId, string label, string? notes, CancellationToken ct = default);

    /// <summary>Deletes a version row (never touches Git). Blocked when deployments reference it.</summary>
    Task<bool> DeleteAsync(Guid versionId, CancellationToken ct = default);
}
