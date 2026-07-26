using Fenrix.IaCStudio.Application.Abstractions.Git;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Providers;
using Fenrix.IaCStudio.Application.Providers;
using Fenrix.IaCStudio.Contracts.Providers;

namespace Fenrix.IaCStudio.Infrastructure.Providers;

/// <summary>
/// Project-scoped façade that resolves a project's bound repository connection, derives the host repo id
/// from the Git remote, and forwards to the matching adapter. Keeps the UI free of factory/secret plumbing.
/// See docs/09-provider-integrations.md.
/// </summary>
public sealed class RepositoryHostService(
    IProjectService projects,
    IRepositoryProviderFactory factory,
    IGitService git) : IRepositoryHostService
{
    private readonly IProjectService _projects = projects;
    private readonly IRepositoryProviderFactory _factory = factory;
    private readonly IGitService _git = git;

    public async Task<ProjectHostBinding> DescribeAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project?.RepositoryConnectionId is not { } connectionId)
            return ProjectHostBinding.None;

        var resolved = await _factory.ResolveAsync(connectionId, ct);
        if (resolved is null)
            return ProjectHostBinding.None;

        var (provider, context) = resolved.Value;

        var remoteUrl = await _git.GetRemoteUrlAsync(projectId, null, ct);
        var repositoryId = RepoUrlParser.RepoId(provider.ProviderType, remoteUrl);

        string? branch = null;
        var info = await _git.DetectAsync(projectId, ct);
        if (info.IsRepository)
            branch = info.CurrentBranch;

        return new ProjectHostBinding(
            HasConnection: true,
            ConnectionId: connectionId,
            ConnectionName: context.DisplayName,
            ProviderType: provider.ProviderType,
            Capabilities: provider.Capabilities,
            HasToken: context.HasToken,
            RepositoryId: repositoryId,
            RepositoryName: RepoUrlParser.RepoName(remoteUrl),
            RemoteOwner: RepoUrlParser.Owner(remoteUrl),
            CurrentBranch: branch);
    }

    public Task<ProviderResult<IReadOnlyList<RemoteRepository>>> GetRepositoriesAsync(
        Guid projectId, CancellationToken ct = default) =>
        WithProvider(projectId, (p, c) => p.GetRepositoriesAsync(c, ct),
            () => ProviderResult<IReadOnlyList<RemoteRepository>>.Fail(ProviderErrorKind.NotSupported, NoConnection));

    public Task<ProviderResult<RemoteRepository>> CreateRepositoryAsync(
        Guid projectId, CreateRepositoryRequest request, CancellationToken ct = default) =>
        WithProvider(projectId, (p, c) => p.CreateRepositoryAsync(c, request, ct),
            () => ProviderResult<RemoteRepository>.Fail(ProviderErrorKind.NotSupported, NoConnection));

    public Task<ProviderResult<IReadOnlyList<PullRequestSummary>>> GetPullRequestsAsync(
        Guid projectId, string repositoryId, CancellationToken ct = default) =>
        WithProvider(projectId, (p, c) => p.GetPullRequestsAsync(c, repositoryId, ct),
            () => ProviderResult<IReadOnlyList<PullRequestSummary>>.Fail(ProviderErrorKind.NotSupported, NoConnection));

    public Task<ProviderResult<PullRequestResult>> CreatePullRequestAsync(
        Guid projectId, string repositoryId, CreatePullRequestRequest request, CancellationToken ct = default) =>
        WithProvider(projectId, (p, c) => p.CreatePullRequestAsync(c, repositoryId, request, ct),
            () => ProviderResult<PullRequestResult>.Fail(ProviderErrorKind.NotSupported, NoConnection));

    public Task<ProviderResult<IReadOnlyList<PipelineRun>>> GetPipelineRunsAsync(
        Guid projectId, string repositoryId, string? branch, CancellationToken ct = default) =>
        WithProvider(projectId, (p, c) => p.GetPipelineRunsAsync(c, repositoryId, branch, ct),
            () => ProviderResult<IReadOnlyList<PipelineRun>>.Fail(ProviderErrorKind.NotSupported, NoConnection));

    public Task<ProviderResult<BranchPolicy>> GetBranchPolicyAsync(
        Guid projectId, string repositoryId, string branch, CancellationToken ct = default) =>
        WithProvider(projectId, (p, c) => p.GetBranchPolicyAsync(c, repositoryId, branch, ct),
            () => ProviderResult<BranchPolicy>.Fail(ProviderErrorKind.NotSupported, NoConnection));

    private const string NoConnection =
        "No repository connection is bound to this project. Bind one from the Provider tab.";

    private async Task<ProviderResult<T>> WithProvider<T>(
        Guid projectId,
        Func<IRepositoryProvider, ProviderConnectionContext, Task<ProviderResult<T>>> call,
        Func<ProviderResult<T>> onUnbound)
    {
        var project = await _projects.GetAsync(projectId);
        if (project?.RepositoryConnectionId is not { } connectionId)
            return onUnbound();

        var resolved = await _factory.ResolveAsync(connectionId);
        if (resolved is null)
            return onUnbound();

        return await call(resolved.Value.Provider, resolved.Value.Context);
    }
}
