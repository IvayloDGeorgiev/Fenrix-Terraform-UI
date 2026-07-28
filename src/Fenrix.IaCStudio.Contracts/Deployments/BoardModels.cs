using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Contracts.Deployments;

/// <summary>
/// A single deployment record projected for the board/history views. Never carries sensitive values — only
/// summaries, hashes, and references. See docs/20-pipelines-deployments.md.
/// </summary>
public sealed record DeploymentSummary(
    Guid Id,
    Guid ProjectId,
    Guid EnvironmentId,
    Guid ProjectVersionId,
    Guid? PlanId,
    string VersionLabel,
    string GitCommit,
    string ShortCommit,
    string GitBranch,
    string TerraformVersion,
    string? StateBackend,
    long? StateSerial,
    string? StateLineage,
    DeploymentStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string InitiatedBy,
    int AddCount,
    int ChangeCount,
    int DestroyCount,
    int ReplaceCount);

/// <summary>
/// One environment stage on the release-pipeline board: the current version deployed, the last successful
/// deployment's summary/state pointer, whether an operation is currently running (env lock), and how far the
/// stage is behind the previous stage. See docs/20-pipelines-deployments.md.
/// </summary>
public sealed record DeploymentBoardStage(
    Guid EnvironmentId,
    string EnvironmentName,
    bool IsProduction,
    int Order,
    ProjectVersionSummary? CurrentVersion,
    DeploymentSummary? LastDeployment,
    bool HasCloudConnection,
    bool IsLocked,
    string? LockDetail,
    int? CommitsBehindPrevious);

/// <summary>The whole board for a project: ordered stages plus the recent deployment history.</summary>
public sealed record DeploymentBoard(
    Guid ProjectId,
    IReadOnlyList<DeploymentBoardStage> Stages,
    IReadOnlyList<DeploymentSummary> RecentDeployments);
