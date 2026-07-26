namespace Fenrix.IaCStudio.Contracts.Providers;

/// <summary>The authenticated identity behind a repository connection (used to confirm a token works).</summary>
public sealed record ProviderUser(
    string Id,
    string UserName,
    string? DisplayName,
    string? AvatarUrl,
    string? ProfileUrl);

/// <summary>
/// A repository on the host, normalised across providers. <see cref="Id"/> is the provider's stable
/// identifier (numeric id, path-with-namespace, ARN, etc.); <see cref="FullName"/> is the human path
/// (owner/repo, workspace/repo, project/repo). See docs/09-provider-integrations.md.
/// </summary>
public sealed record RemoteRepository(
    string Id,
    string Name,
    string FullName,
    string? Description,
    bool IsPrivate,
    string? DefaultBranch,
    string? WebUrl,
    string? CloneUrlHttps,
    string? CloneUrlSsh,
    DateTimeOffset? LastActivityAt);

/// <summary>A request to create a repository on the host.</summary>
public sealed record CreateRepositoryRequest(
    string Name,
    string? Description = null,
    bool IsPrivate = true,
    bool InitializeWithReadme = false,
    string? DefaultBranch = null,
    string? Organisation = null,
    string? ProjectOrWorkspace = null);

/// <summary>Lifecycle state of a pull/merge request, normalised across providers.</summary>
public enum PullRequestState
{
    Open = 0,
    Merged = 1,
    Declined = 2,
    Draft = 3
}

/// <summary>A pull/merge request summary for the list view.</summary>
public sealed record PullRequestSummary(
    string Id,
    long Number,
    string Title,
    PullRequestState State,
    string SourceBranch,
    string TargetBranch,
    string? Author,
    string? WebUrl,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>A request to open a pull/merge request.</summary>
public sealed record CreatePullRequestRequest(
    string Title,
    string SourceBranch,
    string TargetBranch,
    string? Description = null,
    bool Draft = false);

/// <summary>The result of opening a pull/merge request.</summary>
public sealed record PullRequestResult(
    string Id,
    long Number,
    string? WebUrl);

/// <summary>Normalised status of a CI/pipeline / Actions run.</summary>
public enum PipelineStatus
{
    Unknown = 0,
    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Skipped = 6
}

/// <summary>A single CI/pipeline / Actions run, normalised across providers.</summary>
public sealed record PipelineRun(
    string Id,
    string Name,
    PipelineStatus Status,
    string? Branch,
    string? CommitSha,
    string? WebUrl,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt);

/// <summary>
/// Branch-protection / branch-policy rules for a branch, normalised to the common subset providers agree on.
/// Read-only display in Phase 7. See docs/09-provider-integrations.md.
/// </summary>
public sealed record BranchPolicy(
    string Branch,
    bool RequirePullRequest,
    int RequiredApprovals,
    bool RequireStatusChecks,
    bool RequireUpToDate,
    bool RequireLinearHistory,
    bool BlockForcePush,
    bool EnforceForAdmins,
    IReadOnlyList<string> RequiredStatusChecks);
