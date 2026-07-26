using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Application.Abstractions.Providers;

/// <summary>
/// What the SourceControl UI needs to know to show host features for a project: whether a repository
/// connection is bound, which provider/capabilities it has, whether a token is present, and the derived
/// host repository id + current branch (from the project's Git remote). See docs/09-provider-integrations.md.
/// </summary>
public sealed record ProjectHostBinding(
    bool HasConnection,
    Guid? ConnectionId,
    string? ConnectionName,
    RepositoryProviderType ProviderType,
    ProviderCapabilities Capabilities,
    bool HasToken,
    string? RepositoryId,
    string? RepositoryName,
    string? RemoteOwner,
    string? CurrentBranch)
{
    public static ProjectHostBinding None { get; } =
        new(false, null, null, RepositoryProviderType.GenericGit, ProviderCapabilities.None, false, null, null, null, null);
}

/// <summary>
/// Project-scoped façade over <see cref="IRepositoryProviderFactory"/>: resolves the project's bound
/// repository connection, composes the call context (token just-in-time), derives the host repo id from the
/// Git remote, and forwards to the adapter. Every method returns a typed <see cref="ProviderResult{T}"/> so
/// the UI can surface precise guidance; an unbound project or capability-less provider fails cleanly.
/// </summary>
public interface IRepositoryHostService
{
    /// <summary>Describes the project's host binding for the UI (connection, capabilities, repo id, branch).</summary>
    Task<ProjectHostBinding> DescribeAsync(Guid projectId, CancellationToken ct = default);

    Task<ProviderResult<IReadOnlyList<RemoteRepository>>> GetRepositoriesAsync(Guid projectId, CancellationToken ct = default);

    Task<ProviderResult<RemoteRepository>> CreateRepositoryAsync(Guid projectId, CreateRepositoryRequest request, CancellationToken ct = default);

    Task<ProviderResult<IReadOnlyList<PullRequestSummary>>> GetPullRequestsAsync(Guid projectId, string repositoryId, CancellationToken ct = default);

    Task<ProviderResult<PullRequestResult>> CreatePullRequestAsync(Guid projectId, string repositoryId, CreatePullRequestRequest request, CancellationToken ct = default);

    Task<ProviderResult<IReadOnlyList<PipelineRun>>> GetPipelineRunsAsync(Guid projectId, string repositoryId, string? branch, CancellationToken ct = default);

    Task<ProviderResult<BranchPolicy>> GetBranchPolicyAsync(Guid projectId, string repositoryId, string branch, CancellationToken ct = default);
}
