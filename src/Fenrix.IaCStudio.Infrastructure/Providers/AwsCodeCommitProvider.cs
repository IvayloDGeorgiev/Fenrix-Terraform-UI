using Fenrix.IaCStudio.Application.Abstractions.Providers;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Infrastructure.Providers;

/// <summary>
/// AWS CodeCommit adapter. CodeCommit authenticates with IAM (SigV4-signed JSON-RPC) and clones over HTTPS
/// Git credentials, SSH, or <c>git-remote-codecommit</c> — not a personal access token — so its credential
/// story belongs with the Phase 8 cloud-connection work (AWS profiles / SSO), and the SigV4 REST surface
/// (ListRepositories, pull requests) lands there. For Phase 7 this adapter advertises no host API: core Git
/// clone/fetch/pull/push against a CodeCommit remote works normally through the Git engine using the
/// configured AWS credential helper. See docs/09-provider-integrations.md, docs/10-cloud-integrations.md.
/// </summary>
public sealed class AwsCodeCommitProvider : IRepositoryProvider
{
    public RepositoryProviderType ProviderType => RepositoryProviderType.AwsCodeCommit;

    public ProviderCapabilities Capabilities => ProviderCapabilities.None;

    private const string Message =
        "AWS CodeCommit uses IAM (SigV4) and git-remote-codecommit / Git credentials rather than a token. " +
        "Its host API arrives with the Phase 8 AWS cloud-connection work; core Git operations still work now " +
        "using your configured AWS credential helper.";

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
