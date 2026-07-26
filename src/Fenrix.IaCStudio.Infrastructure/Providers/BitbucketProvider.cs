using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Providers;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Providers;

/// <summary>
/// Bitbucket Cloud adapter over REST API 2.0. Authenticates with an app password (Basic, username +
/// app password) or an access token (Bearer when no username is set). The workspace comes from the
/// connection's <c>Organisation</c>; repository ids are the <c>workspace/repo</c> full name. See
/// docs/09-provider-integrations.md.
/// </summary>
public sealed class BitbucketProvider(IHttpClientFactory httpFactory, ILogger<BitbucketProvider> logger)
    : ProviderHttp(httpFactory, logger), IRepositoryProvider
{
    private const string DefaultBase = "https://api.bitbucket.org/2.0";

    public RepositoryProviderType ProviderType => RepositoryProviderType.Bitbucket;

    public ProviderCapabilities Capabilities =>
        ProviderCapabilities.BrowseRepositories | ProviderCapabilities.CreateRepository |
        ProviderCapabilities.ListPullRequests | ProviderCapabilities.CreatePullRequest |
        ProviderCapabilities.PipelineStatus | ProviderCapabilities.BranchPolicies;

    protected override void Authenticate(HttpRequestMessage request, ProviderConnectionContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.UserName))
        {
            var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{context.UserName}:{context.AccessToken}"));
            request.Headers.TryAddWithoutValidation("Authorization", $"Basic {basic}");
        }
        else
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {context.AccessToken}");
        }
    }

    private static string Base(ProviderConnectionContext c) =>
        string.IsNullOrWhiteSpace(c.BaseUrl) ? DefaultBase : c.BaseUrl!.TrimEnd('/');

    public Task<ProviderResult<ProviderUser>> GetCurrentUserAsync(
        ProviderConnectionContext context, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Base(context)}/user"),
            root => new ProviderUser(
                Str(root, "uuid") ?? "",
                Str(root, "username") ?? Str(root, "nickname") ?? "",
                Str(root, "display_name"),
                LinkHref(root, "avatar"),
                LinkHref(root, "html")),
            ct);

    public Task<ProviderResult<IReadOnlyList<RemoteRepository>>> GetRepositoriesAsync(
        ProviderConnectionContext context, CancellationToken ct = default)
    {
        var url = string.IsNullOrWhiteSpace(context.Organisation)
            ? $"{Base(context)}/repositories?role=member&pagelen=100&sort=-updated_on"
            : $"{Base(context)}/repositories/{Uri.EscapeDataString(context.Organisation!)}?pagelen=100&sort=-updated_on";
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            root => (IReadOnlyList<RemoteRepository>)(Child(root, "values") is { } v
                ? Array(v).Select(MapRepo).ToList()
                : []),
            ct);
    }

    public Task<ProviderResult<RemoteRepository>> CreateRepositoryAsync(
        ProviderConnectionContext context, CreateRepositoryRequest request, CancellationToken ct = default)
    {
        var workspace = request.Organisation ?? context.Organisation;
        if (string.IsNullOrWhiteSpace(workspace))
            return Task.FromResult(ProviderResult<RemoteRepository>.Fail(ProviderErrorKind.InvalidRequest,
                "Bitbucket requires a workspace. Set the connection's workspace (Organisation)."));

        var slug = Slug(request.Name);
        var body = new Dictionary<string, object?>
        {
            ["scm"] = "git",
            ["is_private"] = request.IsPrivate,
            ["description"] = request.Description
        };
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Post, $"{Base(context)}/repositories/{Uri.EscapeDataString(workspace!)}/{slug}") { Content = JsonContent.Create(body) },
            MapRepo,
            ct);
    }

    public Task<ProviderResult<IReadOnlyList<PullRequestSummary>>> GetPullRequestsAsync(
        ProviderConnectionContext context, string repositoryId, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Base(context)}/repositories/{repositoryId}/pullrequests?state=OPEN&state=MERGED&state=DECLINED&pagelen=50"),
            root => (IReadOnlyList<PullRequestSummary>)(Child(root, "values") is { } v
                ? Array(v).Select(MapPull).ToList()
                : []),
            ct);

    public Task<ProviderResult<PullRequestResult>> CreatePullRequestAsync(
        ProviderConnectionContext context, string repositoryId, CreatePullRequestRequest request, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["title"] = request.Title,
            ["description"] = request.Description,
            ["source"] = new Dictionary<string, object?> { ["branch"] = new Dictionary<string, object?> { ["name"] = request.SourceBranch } },
            ["destination"] = new Dictionary<string, object?> { ["branch"] = new Dictionary<string, object?> { ["name"] = request.TargetBranch } }
        };
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Post, $"{Base(context)}/repositories/{repositoryId}/pullrequests") { Content = JsonContent.Create(body) },
            root => new PullRequestResult(Long(root, "id").ToString(), Long(root, "id"), LinkHref(root, "html")),
            ct);
    }

    public Task<ProviderResult<IReadOnlyList<PipelineRun>>> GetPipelineRunsAsync(
        ProviderConnectionContext context, string repositoryId, string? branch, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Base(context)}/repositories/{repositoryId}/pipelines?sort=-created_on&pagelen=20"),
            root => (IReadOnlyList<PipelineRun>)(Child(root, "values") is { } v
                ? Array(v).Select(MapPipeline).Where(p => branch is null || string.Equals(p.Branch, branch, StringComparison.OrdinalIgnoreCase)).ToList()
                : []),
            ct);

    public Task<ProviderResult<BranchPolicy>> GetBranchPolicyAsync(
        ProviderConnectionContext context, string repositoryId, string branch, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{Base(context)}/repositories/{repositoryId}/branch-restrictions?pagelen=100"),
            root => MapPolicy(branch, root),
            ct);

    private static RemoteRepository MapRepo(JsonElement e)
    {
        var main = Child(e, "mainbranch");
        return new RemoteRepository(
            Str(e, "full_name") ?? Str(e, "name") ?? "",
            Str(e, "name") ?? "",
            Str(e, "full_name") ?? "",
            Str(e, "description"),
            Bool(e, "is_private"),
            main is { } m ? Str(m, "name") : null,
            LinkHref(e, "html"),
            CloneHref(e, "https"),
            CloneHref(e, "ssh"),
            Date(e, "updated_on"));
    }

    private static PullRequestSummary MapPull(JsonElement e)
    {
        var state = Str(e, "state") switch
        {
            "MERGED" => PullRequestState.Merged,
            "DECLINED" or "SUPERSEDED" => PullRequestState.Declined,
            _ => PullRequestState.Open
        };
        var author = Child(e, "author");
        var source = BranchName(Child(e, "source"));
        var dest = BranchName(Child(e, "destination"));
        return new PullRequestSummary(
            Long(e, "id").ToString(),
            Long(e, "id"),
            Str(e, "title") ?? "",
            state,
            source,
            dest,
            author is { } a ? Str(a, "display_name") : null,
            LinkHref(e, "html"),
            Date(e, "created_on"),
            Date(e, "updated_on"));
    }

    private static PipelineRun MapPipeline(JsonElement e)
    {
        var target = Child(e, "target");
        var stateObj = Child(e, "state");
        var branch = target is { } t ? Str(t, "ref_name") : null;
        var commit = target is { } t2 && Child(t2, "commit") is { } c ? Str(c, "hash") : null;
        return new PipelineRun(
            Str(e, "uuid") ?? Long(e, "build_number").ToString(),
            $"pipeline #{Long(e, "build_number")}",
            MapPipelineState(stateObj),
            branch,
            commit,
            null,
            Date(e, "created_on"),
            Date(e, "completed_on"));
    }

    private static PipelineStatus MapPipelineState(JsonElement? state)
    {
        if (state is not { } s) return PipelineStatus.Unknown;
        var name = Str(s, "name");
        if (name is "PENDING") return PipelineStatus.Queued;
        if (name is "IN_PROGRESS") return PipelineStatus.Running;
        if (name is "COMPLETED")
        {
            var result = Child(s, "result");
            return (result is { } r ? Str(r, "name") : null) switch
            {
                "SUCCESSFUL" => PipelineStatus.Succeeded,
                "FAILED" or "ERROR" => PipelineStatus.Failed,
                "STOPPED" => PipelineStatus.Cancelled,
                _ => PipelineStatus.Unknown
            };
        }
        return PipelineStatus.Unknown;
    }

    private static BranchPolicy MapPolicy(string branch, JsonElement root)
    {
        var values = Child(root, "values") is { } v ? Array(v).ToList() : [];
        bool Match(JsonElement r) => Matches(Str(r, "pattern"), branch);

        var relevant = values.Where(Match).ToList();
        var kinds = relevant.Select(r => Str(r, "kind")).Where(k => k is not null).ToHashSet();
        var approvals = relevant
            .Where(r => Str(r, "kind") == "require_approvals_to_merge")
            .Select(r => (int)Long(r, "value"))
            .DefaultIfEmpty(0)
            .Max();

        return new BranchPolicy(
            branch,
            RequirePullRequest: kinds.Contains("require_approvals_to_merge") || kinds.Contains("enforce_merge_checks"),
            RequiredApprovals: approvals,
            RequireStatusChecks: kinds.Contains("require_passing_builds_to_merge"),
            RequireUpToDate: false,
            RequireLinearHistory: false,
            BlockForcePush: kinds.Contains("force"),
            EnforceForAdmins: false,
            RequiredStatusChecks: []);
    }

    private static bool Matches(string? pattern, string branch)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (pattern == branch || pattern == "*" || pattern == "**") return true;
        if (pattern.EndsWith('*'))
            return branch.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static string BranchName(JsonElement? side) =>
        side is { } s && Child(s, "branch") is { } b ? Str(b, "name") ?? "" : "";

    private static string? LinkHref(JsonElement e, string name) =>
        Child(e, "links") is { } links && Child(links, name) is { } link ? Str(link, "href") : null;

    private static string? CloneHref(JsonElement e, string kind)
    {
        if (Child(e, "links") is not { } links || Child(links, "clone") is not { } clone)
            return null;
        foreach (var entry in Array(clone))
            if (string.Equals(Str(entry, "name"), kind, StringComparison.OrdinalIgnoreCase))
                return Str(entry, "href");
        return null;
    }

    private static string Slug(string name) =>
        new string(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray())
            .Trim('-');
}
