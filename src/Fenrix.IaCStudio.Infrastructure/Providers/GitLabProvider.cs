using System.Net.Http.Json;
using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Providers;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Providers;

/// <summary>
/// GitLab adapter (gitlab.com and self-managed) over REST API v4. Uses merge-request terminology. Repository
/// ids are the numeric project id, which composes into the <c>/projects/{id}/…</c> paths. See
/// docs/09-provider-integrations.md.
/// </summary>
public sealed class GitLabProvider(IHttpClientFactory httpFactory, ILogger<GitLabProvider> logger)
    : ProviderHttp(httpFactory, logger), IRepositoryProvider
{
    private const string DefaultBase = "https://gitlab.com";

    public RepositoryProviderType ProviderType => RepositoryProviderType.GitLab;

    public ProviderCapabilities Capabilities =>
        ProviderCapabilities.BrowseRepositories | ProviderCapabilities.CreateRepository |
        ProviderCapabilities.ListPullRequests | ProviderCapabilities.CreatePullRequest |
        ProviderCapabilities.PipelineStatus | ProviderCapabilities.BranchPolicies |
        ProviderCapabilities.UsesMergeRequestTerminology;

    protected override void Authenticate(HttpRequestMessage request, ProviderConnectionContext context) =>
        request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", context.AccessToken);

    private static string Api(ProviderConnectionContext c) =>
        (string.IsNullOrWhiteSpace(c.BaseUrl) ? DefaultBase : c.BaseUrl!.TrimEnd('/')) + "/api/v4";

    public Task<ProviderResult<ProviderUser>> GetCurrentUserAsync(
        ProviderConnectionContext context, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Api(context)}/user"),
            root => new ProviderUser(
                Long(root, "id").ToString(),
                Str(root, "username") ?? "",
                Str(root, "name"),
                Str(root, "avatar_url"),
                Str(root, "web_url")),
            ct);

    public Task<ProviderResult<IReadOnlyList<RemoteRepository>>> GetRepositoriesAsync(
        ProviderConnectionContext context, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Api(context)}/projects?membership=true&per_page=100&order_by=last_activity_at"),
            root => (IReadOnlyList<RemoteRepository>)Array(root).Select(MapRepo).ToList(),
            ct);

    public Task<ProviderResult<RemoteRepository>> CreateRepositoryAsync(
        ProviderConnectionContext context, CreateRepositoryRequest request, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = request.Name,
            ["description"] = request.Description,
            ["visibility"] = request.IsPrivate ? "private" : "public",
            ["initialize_with_readme"] = request.InitializeWithReadme
        };
        if (!string.IsNullOrWhiteSpace(request.DefaultBranch)) body["default_branch"] = request.DefaultBranch;
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Post, $"{Api(context)}/projects") { Content = JsonContent.Create(body) },
            MapRepo,
            ct);
    }

    public Task<ProviderResult<IReadOnlyList<PullRequestSummary>>> GetPullRequestsAsync(
        ProviderConnectionContext context, string repositoryId, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Api(context)}/projects/{Uri.EscapeDataString(repositoryId)}/merge_requests?state=all&per_page=50"),
            root => (IReadOnlyList<PullRequestSummary>)Array(root).Select(MapMr).ToList(),
            ct);

    public Task<ProviderResult<PullRequestResult>> CreatePullRequestAsync(
        ProviderConnectionContext context, string repositoryId, CreatePullRequestRequest request, CancellationToken ct = default)
    {
        var title = request.Draft ? $"Draft: {request.Title}" : request.Title;
        var body = new Dictionary<string, object?>
        {
            ["source_branch"] = request.SourceBranch,
            ["target_branch"] = request.TargetBranch,
            ["title"] = title,
            ["description"] = request.Description
        };
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Post, $"{Api(context)}/projects/{Uri.EscapeDataString(repositoryId)}/merge_requests") { Content = JsonContent.Create(body) },
            root => new PullRequestResult(Long(root, "id").ToString(), Long(root, "iid"), Str(root, "web_url")),
            ct);
    }

    public Task<ProviderResult<IReadOnlyList<PipelineRun>>> GetPipelineRunsAsync(
        ProviderConnectionContext context, string repositoryId, string? branch, CancellationToken ct = default)
    {
        var url = $"{Api(context)}/projects/{Uri.EscapeDataString(repositoryId)}/pipelines?per_page=20";
        if (!string.IsNullOrWhiteSpace(branch)) url += $"&ref={Uri.EscapeDataString(branch)}";
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            root => (IReadOnlyList<PipelineRun>)Array(root).Select(MapPipeline).ToList(),
            ct);
    }

    public Task<ProviderResult<BranchPolicy>> GetBranchPolicyAsync(
        ProviderConnectionContext context, string repositoryId, string branch, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Api(context)}/projects/{Uri.EscapeDataString(repositoryId)}/protected_branches/{Uri.EscapeDataString(branch)}"),
            root => new BranchPolicy(
                Str(root, "name") ?? branch,
                RequirePullRequest: true, // GitLab enforces changes through merge requests
                RequiredApprovals: 0,     // approval rules are a separate resource
                RequireStatusChecks: false,
                RequireUpToDate: false,
                RequireLinearHistory: false,
                BlockForcePush: !Bool(root, "allow_force_push"),
                EnforceForAdmins: Bool(root, "code_owner_approval_required"),
                RequiredStatusChecks: []),
            ct);

    private static RemoteRepository MapRepo(JsonElement e) => new(
        Long(e, "id").ToString(),
        Str(e, "name") ?? "",
        Str(e, "path_with_namespace") ?? Str(e, "name") ?? "",
        Str(e, "description"),
        Str(e, "visibility") != "public",
        Str(e, "default_branch"),
        Str(e, "web_url"),
        Str(e, "http_url_to_repo"),
        Str(e, "ssh_url_to_repo"),
        Date(e, "last_activity_at"));

    private static PullRequestSummary MapMr(JsonElement e)
    {
        var state = Bool(e, "draft") || Bool(e, "work_in_progress") ? PullRequestState.Draft
            : Str(e, "state") switch
            {
                "merged" => PullRequestState.Merged,
                "closed" or "locked" => PullRequestState.Declined,
                _ => PullRequestState.Open
            };
        var author = Child(e, "author");
        return new PullRequestSummary(
            Long(e, "id").ToString(),
            Long(e, "iid"),
            Str(e, "title") ?? "",
            state,
            Str(e, "source_branch") ?? "",
            Str(e, "target_branch") ?? "",
            author is { } a ? Str(a, "username") : null,
            Str(e, "web_url"),
            Date(e, "created_at"),
            Date(e, "updated_at"));
    }

    private static PipelineRun MapPipeline(JsonElement e) => new(
        Long(e, "id").ToString(),
        $"pipeline #{Long(e, "id")}",
        MapStatus(Str(e, "status")),
        Str(e, "ref"),
        Str(e, "sha"),
        Str(e, "web_url"),
        Date(e, "created_at"),
        Date(e, "updated_at"));

    private static PipelineStatus MapStatus(string? status) => status switch
    {
        "created" or "waiting_for_resource" or "preparing" or "pending" or "scheduled" or "manual" => PipelineStatus.Queued,
        "running" => PipelineStatus.Running,
        "success" => PipelineStatus.Succeeded,
        "failed" => PipelineStatus.Failed,
        "canceled" or "cancelled" => PipelineStatus.Cancelled,
        "skipped" => PipelineStatus.Skipped,
        _ => PipelineStatus.Unknown
    };
}
