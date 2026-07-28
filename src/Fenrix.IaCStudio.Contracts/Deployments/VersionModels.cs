namespace Fenrix.IaCStudio.Contracts.Deployments;

/// <summary>
/// A read model for a project version on the Pipelines UI. Anchored to an immutable Git snapshot; a version
/// belongs to the project and can be deployed to any/all environments independently.
/// See docs/20-pipelines-deployments.md.
/// </summary>
public sealed record ProjectVersionSummary(
    Guid Id,
    Guid ProjectId,
    string Label,
    string GitCommit,
    string ShortCommit,
    string? GitTag,
    string? GitBranch,
    string? RequiredTerraformVersion,
    string? Notes,
    DateTimeOffset CreatedAt,
    string CreatedBy);

/// <summary>
/// A request to "cut a version" from the project's current Git HEAD. Optionally pushes an annotated tag with
/// the same label. The commit, config hash, and provider-lock hash are captured by the service, not supplied
/// here. See docs/20-pipelines-deployments.md.
/// </summary>
public sealed record CutVersionRequest(
    Guid ProjectId,
    string Label,
    string? Notes = null,
    bool CreateGitTag = false,
    bool PushGitTag = false);

/// <summary>Outcome of cutting a version, including any Git-tag warning (e.g. dirty tree, push failed).</summary>
public sealed record CutVersionResult(
    bool Succeeded,
    ProjectVersionSummary? Version,
    string? Warning,
    string? Error)
{
    public static CutVersionResult Fail(string error) => new(false, null, null, error);
}
