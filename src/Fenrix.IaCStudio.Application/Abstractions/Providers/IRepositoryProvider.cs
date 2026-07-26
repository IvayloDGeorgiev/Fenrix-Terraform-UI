using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Application.Abstractions.Providers;

/// <summary>
/// A thin, host-specific adapter over a version-control provider's REST API. Core Git stays
/// provider-independent (driven by <c>git.exe</c>); this covers only what Git itself cannot do — browsing and
/// creating remote repositories, pull/merge requests, pipeline status, and branch policies. Every method
/// takes the transient <see cref="ProviderConnectionContext"/> (host + just-in-time token) and returns a
/// typed <see cref="ProviderResult{T}"/> rather than throwing, so the UI can surface precise auth/rate-limit
/// guidance. Unsupported operations return <see cref="ProviderErrorKind.NotSupported"/>. See
/// docs/09-provider-integrations.md.
/// </summary>
public interface IRepositoryProvider
{
    /// <summary>The provider this adapter serves.</summary>
    RepositoryProviderType ProviderType { get; }

    /// <summary>Which host features this adapter supports, so the UI only shows what works.</summary>
    ProviderCapabilities Capabilities { get; }

    /// <summary>Confirms the token works and returns the identity behind it (the "Test connection" call).</summary>
    Task<ProviderResult<ProviderUser>> GetCurrentUserAsync(
        ProviderConnectionContext context, CancellationToken ct = default);

    /// <summary>Lists repositories visible to the connection (owned + org/workspace/project scope).</summary>
    Task<ProviderResult<IReadOnlyList<RemoteRepository>>> GetRepositoriesAsync(
        ProviderConnectionContext context, CancellationToken ct = default);

    /// <summary>Creates a repository on the host.</summary>
    Task<ProviderResult<RemoteRepository>> CreateRepositoryAsync(
        ProviderConnectionContext context, CreateRepositoryRequest request, CancellationToken ct = default);

    /// <summary>Lists pull/merge requests for a repository.</summary>
    Task<ProviderResult<IReadOnlyList<PullRequestSummary>>> GetPullRequestsAsync(
        ProviderConnectionContext context, string repositoryId, CancellationToken ct = default);

    /// <summary>Opens a pull/merge request on a repository.</summary>
    Task<ProviderResult<PullRequestResult>> CreatePullRequestAsync(
        ProviderConnectionContext context, string repositoryId, CreatePullRequestRequest request, CancellationToken ct = default);

    /// <summary>Reads the most recent CI/pipeline / Actions runs for a repository (optionally a branch).</summary>
    Task<ProviderResult<IReadOnlyList<PipelineRun>>> GetPipelineRunsAsync(
        ProviderConnectionContext context, string repositoryId, string? branch, CancellationToken ct = default);

    /// <summary>Reads branch-protection / branch-policy rules for a branch (read-only display in Phase 7).</summary>
    Task<ProviderResult<BranchPolicy>> GetBranchPolicyAsync(
        ProviderConnectionContext context, string repositoryId, string branch, CancellationToken ct = default);
}
