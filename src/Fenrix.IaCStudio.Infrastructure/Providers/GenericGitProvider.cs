using Fenrix.IaCStudio.Application.Abstractions.Providers;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Infrastructure.Providers;

/// <summary>
/// The fall-back adapter for any Git remote without a host-specific integration (self-hosted servers, Gitea,
/// Forgejo, unknown hosts). It advertises <see cref="ProviderCapabilities.None"/> — normal Git still works
/// fully through the Git engine, but host features (repo browse/create, PRs, pipelines, policies) are
/// unavailable and every call returns <see cref="ProviderErrorKind.NotSupported"/>. See
/// docs/09-provider-integrations.md.
/// </summary>
public sealed class GenericGitProvider : IRepositoryProvider
{
    public RepositoryProviderType ProviderType => RepositoryProviderType.GenericGit;

    public ProviderCapabilities Capabilities => ProviderCapabilities.None;

    private const string Message = "This remote has no host adapter. Core Git operations still work for this repository.";

    public Task<ProviderResult<ProviderUser>> GetCurrentUserAsync(
        ProviderConnectionContext context, CancellationToken ct = default) =>
        Task.FromResult(ProviderResult<ProviderUser>.Fail(ProviderErrorKind.NotSupported, Message));

    public Task<ProviderResult<IReadOnlyList<RemoteRepository>>> GetRepositoriesAsync(
        ProviderConnectionContext context, CancellationToken ct = default) =>
        Task.FromResult(ProviderResult<IReadOnlyList<RemoteRepository>>.Fail(ProviderErrorKind.NotSupported, Message));

    public Task<ProviderResult<RemoteRepository>> CreateRepositoryAsync(
        ProviderConnectionContext context, CreateRepositoryRequest request, CancellationToken ct = default) =>
        Task.FromResult(ProviderResult<RemoteRepository>.Fail(ProviderErrorKind.NotSupported, Message));

    public Task<ProviderResult<IReadOnlyList<PullRequestSummary>>> GetPullRequestsAsync(
        ProviderConnectionContext context, string repositoryId, CancellationToken ct = default) =>
        Task.FromResult(ProviderResult<IReadOnlyList<PullRequestSummary>>.Fail(ProviderErrorKind.NotSupported, Message));

    public Task<ProviderResult<PullRequestResult>> CreatePullRequestAsync(
        ProviderConnectionContext context, string repositoryId, CreatePullRequestRequest request, CancellationToken ct = default) =>
        Task.FromResult(ProviderResult<PullRequestResult>.Fail(ProviderErrorKind.NotSupported, Message));

    public Task<ProviderResult<IReadOnlyList<PipelineRun>>> GetPipelineRunsAsync(
        ProviderConnectionContext context, string repositoryId, string? branch, CancellationToken ct = default) =>
        Task.FromResult(ProviderResult<IReadOnlyList<PipelineRun>>.Fail(ProviderErrorKind.NotSupported, Message));

    public Task<ProviderResult<BranchPolicy>> GetBranchPolicyAsync(
        ProviderConnectionContext context, string repositoryId, string branch, CancellationToken ct = default) =>
        Task.FromResult(ProviderResult<BranchPolicy>.Fail(ProviderErrorKind.NotSupported, Message));
}
