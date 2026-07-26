using System.Net.Http.Json;
using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Providers;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Providers;

/// <summary>
/// GitHub (and GitHub Enterprise) adapter over the versioned REST API. Repository ids are the
/// <c>owner/repo</c> full name so they compose directly into resource paths. Supports repo browse/create,
/// pull requests, Actions run status, and branch protection. See docs/09-provider-integrations.md.
/// </summary>
public sealed class GitHubProvider(IHttpClientFactory httpFactory, ILogger<GitHubProvider> logger)
    : ProviderHttp(httpFactory, logger), IRepositoryProvider
{
    private const string ApiVersion = "2022-11-28";
    private const string DefaultBase = "https://api.github.com";

    public RepositoryProviderType ProviderType => RepositoryProviderType.GitHub;

    public ProviderCapabilities Capabilities =>
        ProviderCapabilities.BrowseRepositories | ProviderCapabilities.CreateRepository |
        ProviderCapabilities.ListPullRequests | ProviderCapabilities.CreatePullRequest |
        ProviderCapabilities.PipelineStatus | ProviderCapabilities.BranchPolicies;

    protected override void Authenticate(HttpRequestMessage request, ProviderConnectionContext context)
    {
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {context.AccessToken}");
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
    }

    private static string Base(ProviderConnectionContext c) =>
        string.IsNullOrWhiteSpace(c.BaseUrl) ? DefaultBase : c.BaseUrl!.TrimEnd('/');

    public Task<ProviderResult<ProviderUser>> GetCurrentUserAsync(
        ProviderConnectionContext context, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Base(context)}/user"),
            root => new ProviderUser(
                Long(root, "id").ToString(),
                Str(root, "login") ?? "",
                Str(root, "name"),
                Str(root, "avatar_url"),
                Str(root, "html_url")),
            ct);

    public Task<ProviderResult<IReadOnlyList<RemoteRepository>>> GetRepositoriesAsync(
        ProviderConnectionContext context, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(context.Organisation)
            ? $"{Base(context)}/user/repos?per_page=100&sort=updated&affiliation=owner,collaborator,organization_member"
            : $"{Base(context)}/orgs/{context.Organisation}/repos?per_page=100&sort=updated";
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            root => (IReadOnlyList<RemoteRepository>)Array(root).Select(MapRepo).ToList(),
            ct);
    }

    public Task<ProviderResult<RemoteRepository>> CreateRepositoryAsync(
        ProviderConnectionContext context, CreateRepositoryRequest request, CancellationToken ct = default)
    {
        var org = request.Organisation ?? context.Organisation;
        var url = string.IsNullOrWhiteSpace(org)
            ? $"{Base(context)}/user/repos"
            : $"{Base(context)}/orgs/{org}/repos";
        var body = new Dictionary<string, object?>
        {
            ["name"] = request.Name,
            ["description"] = request.Description,
            ["private"] = request.IsPrivate,
            ["auto_init"] = request.InitializeWithReadme
        };
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) },
            MapRepo,
            ct);
    }

    public Task<ProviderResult<IReadOnlyList<PullRequestSummary>>> GetPullRequestsAsync(
        ProviderConnectionContext context, string repositoryId, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Base(context)}/repos/{repositoryId}/pulls?state=all&per_page=50"),
            root => (IReadOnlyList<PullRequestSummary>)Array(root).Select(MapPull).ToList(),
            ct);

    public Task<ProviderResult<PullRequestResult>> CreatePullRequestAsync(
        ProviderConnectionContext context, string repositoryId, CreatePullRequestRequest request, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = request.Title,
            ["head"] = request.SourceBranch,
            ["base"] = request.TargetBranch,
            ["body"] = request.Description,
            ["draft"] = request.Draft
        };
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Post, $"{Base(context)}/repos/{repositoryId}/pulls") { Content = JsonContent.Create(body) },
            root => new PullRequestResult(Long(root, "id").ToString(), Long(root, "number"), Str(root, "html_url")),
            ct);
    }

    public Task<ProviderResult<IReadOnlyList<PipelineRun>>> GetPipelineRunsAsync(
        ProviderConnectionContext context, string repositoryId, string? branch, CancellationToken ct = default)
    {
        var url = $"{Base(context)}/repos/{repositoryId}/actions/runs?per_page=20";
        if (!string.IsNullOrWhiteSpace(branch)) url += $"&branch={Uri.EscapeDataString(branch)}";
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            root => (IReadOnlyList<PipelineRun>)(Child(root, "workflow_runs") is { } runs
                ? Array(runs).Select(MapRun).ToList()
                : []),
            ct);
    }

    public Task<ProviderResult<BranchPolicy>> GetBranchPolicyAsync(
        ProviderConnectionContext context, string repositoryId, string branch, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Base(context)}/repos/{repositoryId}/branches/{Uri.EscapeDataString(branch)}/protection"),
            root => MapPolicy(branch, root),
            ct);

    // ---- mapping ----

    private static RemoteRepository MapRepo(JsonElement e) => new(
        Str(e, "full_name") ?? Str(e, "name") ?? Long(e, "id").ToString(),
        Str(e, "name") ?? "",
        Str(e, "full_name") ?? "",
        Str(e, "description"),
        Bool(e, "private"),
        Str(e, "default_branch"),
        Str(e, "html_url"),
        Str(e, "clone_url"),
        Str(e, "ssh_url"),
        Date(e, "pushed_at") ?? Date(e, "updated_at"));

    private static PullRequestSummary MapPull(JsonElement e)
    {
        var state = Bool(e, "draft") ? PullRequestState.Draft
            : Date(e, "merged_at") is not null ? PullRequestState.Merged
            : Str(e, "state") == "closed" ? PullRequestState.Declined
            : PullRequestState.Open;
        var head = Child(e, "head");
        var baseRef = Child(e, "base");
        var user = Child(e, "user");
        return new PullRequestSummary(
            Long(e, "id").ToString(),
            Long(e, "number"),
            Str(e, "title") ?? "",
            state,
            head is { } h ? Str(h, "ref") ?? "" : "",
            baseRef is { } b ? Str(b, "ref") ?? "" : "",
            user is { } u ? Str(u, "login") : null,
            Str(e, "html_url"),
            Date(e, "created_at"),
            Date(e, "updated_at"));
    }

    private static PipelineRun MapRun(JsonElement e)
    {
        var status = MapRunStatus(Str(e, "status"), Str(e, "conclusion"));
        return new PipelineRun(
            Long(e, "id").ToString(),
            Str(e, "name") ?? Str(e, "display_title") ?? "workflow",
            status,
            Str(e, "head_branch"),
            Str(e, "head_sha"),
            Str(e, "html_url"),
            Date(e, "run_started_at") ?? Date(e, "created_at"),
            Date(e, "updated_at"));
    }

    private static PipelineStatus MapRunStatus(string? status, string? conclusion) => status switch
    {
        "queued" or "pending" or "waiting" or "requested" => PipelineStatus.Queued,
        "in_progress" => PipelineStatus.Running,
        "completed" => conclusion switch
        {
            "success" => PipelineStatus.Succeeded,
            "failure" or "timed_out" or "startup_failure" => PipelineStatus.Failed,
            "cancelled" => PipelineStatus.Cancelled,
            "skipped" or "neutral" or "stale" => PipelineStatus.Skipped,
            _ => PipelineStatus.Unknown
        },
        _ => PipelineStatus.Unknown
    };

    private static BranchPolicy MapPolicy(string branch, JsonElement e)
    {
        var reviews = Child(e, "required_pull_request_reviews");
        var checks = Child(e, "required_status_checks");
        var checkContexts = checks is { } c && Child(c, "contexts") is { } ctx
            ? Array(ctx).Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToList()
            : [];
        return new BranchPolicy(
            branch,
            RequirePullRequest: reviews is not null,
            RequiredApprovals: reviews is { } r ? (int)Long(r, "required_approving_review_count") : 0,
            RequireStatusChecks: checks is not null,
            RequireUpToDate: checks is { } sc && Bool(sc, "strict"),
            RequireLinearHistory: Child(e, "required_linear_history") is { } lin && Bool(lin, "enabled"),
            BlockForcePush: Child(e, "allow_force_pushes") is { } afp && !Bool(afp, "enabled"),
            EnforceForAdmins: Child(e, "enforce_admins") is { } ea && Bool(ea, "enabled"),
            RequiredStatusChecks: checkContexts);
    }
}
