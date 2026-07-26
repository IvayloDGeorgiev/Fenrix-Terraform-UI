using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fenrix.IaCStudio.Application.Abstractions.Providers;
using Fenrix.IaCStudio.Contracts.Providers;
using Fenrix.IaCStudio.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Providers;

/// <summary>
/// Azure DevOps adapter over the REST APIs. Authenticates with a PAT via Basic auth (empty username). The
/// organisation is taken from the connection's <c>Organisation</c> and the team project from
/// <c>ProjectOrWorkspace</c>; repository ids are the repo GUID. Supports repo browse/create, pull requests,
/// build/pipeline status, and a best-effort branch-policy read. See docs/09-provider-integrations.md.
/// </summary>
public sealed class AzureDevOpsProvider(IHttpClientFactory httpFactory, ILogger<AzureDevOpsProvider> logger)
    : ProviderHttp(httpFactory, logger), IRepositoryProvider
{
    private const string DefaultBase = "https://dev.azure.com";
    private const string ApiVersion = "api-version=7.1";

    public RepositoryProviderType ProviderType => RepositoryProviderType.AzureDevOps;

    public ProviderCapabilities Capabilities =>
        ProviderCapabilities.BrowseRepositories | ProviderCapabilities.CreateRepository |
        ProviderCapabilities.ListPullRequests | ProviderCapabilities.CreatePullRequest |
        ProviderCapabilities.PipelineStatus | ProviderCapabilities.BranchPolicies;

    protected override void Authenticate(HttpRequestMessage request, ProviderConnectionContext context)
    {
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{context.AccessToken}"));
        request.Headers.TryAddWithoutValidation("Authorization", $"Basic {basic}");
    }

    private static string OrgBase(ProviderConnectionContext c) =>
        (string.IsNullOrWhiteSpace(c.BaseUrl) ? DefaultBase : c.BaseUrl!.TrimEnd('/')) + "/" +
        (c.Organisation ?? "").Trim('/');

    public Task<ProviderResult<ProviderUser>> GetCurrentUserAsync(
        ProviderConnectionContext context, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{OrgBase(context)}/_apis/connectionData?{ApiVersion}"),
            root =>
            {
                var user = Child(root, "authenticatedUser");
                return new ProviderUser(
                    user is { } u ? Str(u, "id") ?? "" : "",
                    user is { } u2 ? Str(u2, "providerDisplayName") ?? "" : "",
                    user is { } u3 ? Str(u3, "providerDisplayName") : null,
                    null, null);
            },
            ct);

    public Task<ProviderResult<IReadOnlyList<RemoteRepository>>> GetRepositoriesAsync(
        ProviderConnectionContext context, CancellationToken ct = default)
    {
        var scope = string.IsNullOrWhiteSpace(context.ProjectOrWorkspace)
            ? OrgBase(context)
            : $"{OrgBase(context)}/{Uri.EscapeDataString(context.ProjectOrWorkspace!)}";
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{scope}/_apis/git/repositories?{ApiVersion}"),
            root => (IReadOnlyList<RemoteRepository>)(Child(root, "value") is { } v
                ? Array(v).Select(MapRepo).ToList()
                : []),
            ct);
    }

    public Task<ProviderResult<RemoteRepository>> CreateRepositoryAsync(
        ProviderConnectionContext context, CreateRepositoryRequest request, CancellationToken ct = default)
    {
        var project = request.ProjectOrWorkspace ?? context.ProjectOrWorkspace;
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult(ProviderResult<RemoteRepository>.Fail(ProviderErrorKind.InvalidRequest,
                "Azure DevOps requires a team project. Set the connection's project (ProjectOrWorkspace)."));

        var body = new Dictionary<string, object?>
        {
            ["name"] = request.Name,
            ["project"] = new Dictionary<string, object?> { ["name"] = project }
        };
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Post, $"{OrgBase(context)}/{Uri.EscapeDataString(project!)}/_apis/git/repositories?{ApiVersion}") { Content = JsonContent.Create(body) },
            MapRepo,
            ct);
    }

    public Task<ProviderResult<IReadOnlyList<PullRequestSummary>>> GetPullRequestsAsync(
        ProviderConnectionContext context, string repositoryId, CancellationToken ct = default) =>
        SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{OrgBase(context)}/_apis/git/repositories/{repositoryId}/pullrequests?searchCriteria.status=all&{ApiVersion}"),
            root => (IReadOnlyList<PullRequestSummary>)(Child(root, "value") is { } v
                ? Array(v).Select(MapPull).ToList()
                : []),
            ct);

    public Task<ProviderResult<PullRequestResult>> CreatePullRequestAsync(
        ProviderConnectionContext context, string repositoryId, CreatePullRequestRequest request, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?>
        {
            ["sourceRefName"] = $"refs/heads/{request.SourceBranch}",
            ["targetRefName"] = $"refs/heads/{request.TargetBranch}",
            ["title"] = request.Title,
            ["description"] = request.Description,
            ["isDraft"] = request.Draft
        };
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Post, $"{OrgBase(context)}/_apis/git/repositories/{repositoryId}/pullrequests?{ApiVersion}") { Content = JsonContent.Create(body) },
            root => new PullRequestResult(Long(root, "pullRequestId").ToString(), Long(root, "pullRequestId"), WebLink(root)),
            ct);
    }

    public Task<ProviderResult<IReadOnlyList<PipelineRun>>> GetPipelineRunsAsync(
        ProviderConnectionContext context, string repositoryId, string? branch, CancellationToken ct = default)
    {
        var project = context.ProjectOrWorkspace;
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult(ProviderResult<IReadOnlyList<PipelineRun>>.Fail(ProviderErrorKind.InvalidRequest,
                "Azure DevOps build status requires the connection's team project (ProjectOrWorkspace)."));

        var url = $"{OrgBase(context)}/{Uri.EscapeDataString(project!)}/_apis/build/builds?$top=20&{ApiVersion}";
        if (!string.IsNullOrWhiteSpace(branch)) url += $"&branchName=refs/heads/{Uri.EscapeDataString(branch)}";
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, url),
            root => (IReadOnlyList<PipelineRun>)(Child(root, "value") is { } v
                ? Array(v).Select(MapBuild).ToList()
                : []),
            ct);
    }

    public Task<ProviderResult<BranchPolicy>> GetBranchPolicyAsync(
        ProviderConnectionContext context, string repositoryId, string branch, CancellationToken ct = default)
    {
        var project = context.ProjectOrWorkspace;
        if (string.IsNullOrWhiteSpace(project))
            return Task.FromResult(ProviderResult<BranchPolicy>.Fail(ProviderErrorKind.InvalidRequest,
                "Azure DevOps branch policies require the connection's team project (ProjectOrWorkspace)."));

        var refName = $"refs/heads/{branch}";
        return SendAsync(context,
            () => new HttpRequestMessage(HttpMethod.Get, $"{OrgBase(context)}/{Uri.EscapeDataString(project!)}/_apis/git/policy/configurations?repositoryId={repositoryId}&refName={Uri.EscapeDataString(refName)}&{ApiVersion}"),
            root => MapPolicy(branch, root),
            ct);
    }

    private static RemoteRepository MapRepo(JsonElement e)
    {
        var project = Child(e, "project");
        var projectName = project is { } p ? Str(p, "name") : null;
        var name = Str(e, "name") ?? "";
        return new RemoteRepository(
            Str(e, "id") ?? name,
            name,
            projectName is null ? name : $"{projectName}/{name}",
            null,
            Bool(e, "isPrivate") || project is not null, // Azure DevOps repos are private by default
            StripRef(Str(e, "defaultBranch")),
            Str(e, "webUrl"),
            Str(e, "remoteUrl"),
            Str(e, "sshUrl"),
            null);
    }

    private static PullRequestSummary MapPull(JsonElement e)
    {
        var state = Bool(e, "isDraft") ? PullRequestState.Draft
            : Str(e, "status") switch
            {
                "completed" => PullRequestState.Merged,
                "abandoned" => PullRequestState.Declined,
                _ => PullRequestState.Open
            };
        var author = Child(e, "createdBy");
        return new PullRequestSummary(
            Long(e, "pullRequestId").ToString(),
            Long(e, "pullRequestId"),
            Str(e, "title") ?? "",
            state,
            StripRef(Str(e, "sourceRefName")) ?? "",
            StripRef(Str(e, "targetRefName")) ?? "",
            author is { } a ? Str(a, "displayName") : null,
            WebLink(e),
            Date(e, "creationDate"),
            Date(e, "creationDate"));
    }

    private static PipelineRun MapBuild(JsonElement e) => new(
        Long(e, "id").ToString(),
        Str(e, "buildNumber") ?? $"build {Long(e, "id")}",
        MapBuildStatus(Str(e, "status"), Str(e, "result")),
        StripRef(Str(e, "sourceBranch")),
        Str(e, "sourceVersion"),
        WebLink(e),
        Date(e, "startTime") ?? Date(e, "queueTime"),
        Date(e, "finishTime"));

    private static PipelineStatus MapBuildStatus(string? status, string? result) => status switch
    {
        "notStarted" or "postponed" => PipelineStatus.Queued,
        "inProgress" => PipelineStatus.Running,
        "completed" => result switch
        {
            "succeeded" => PipelineStatus.Succeeded,
            "partiallySucceeded" => PipelineStatus.Succeeded,
            "failed" => PipelineStatus.Failed,
            "canceled" => PipelineStatus.Cancelled,
            _ => PipelineStatus.Unknown
        },
        _ => PipelineStatus.Unknown
    };

    // "Minimum number of reviewers" policy type id.
    private const string MinReviewersType = "fa4e907d-c16b-4a4c-9dfa-4906e5d171dd";

    private static BranchPolicy MapPolicy(string branch, JsonElement root)
    {
        var configs = Child(root, "value") is { } v ? Array(v).ToList() : [];
        var requireReviewers = false;
        var approvals = 0;
        foreach (var cfg in configs)
        {
            var type = Child(cfg, "type");
            var typeId = type is { } t ? Str(t, "id") : null;
            if (string.Equals(typeId, MinReviewersType, StringComparison.OrdinalIgnoreCase))
            {
                requireReviewers = true;
                if (Child(cfg, "settings") is { } s)
                    approvals = (int)Long(s, "minimumApproverCount");
            }
        }
        return new BranchPolicy(
            branch,
            RequirePullRequest: configs.Count > 0,
            RequiredApprovals: approvals,
            RequireStatusChecks: false,
            RequireUpToDate: false,
            RequireLinearHistory: false,
            BlockForcePush: configs.Count > 0,
            EnforceForAdmins: requireReviewers,
            RequiredStatusChecks: []);
    }

    private static string? StripRef(string? refName) =>
        string.IsNullOrEmpty(refName) ? refName
        : refName.StartsWith("refs/heads/", StringComparison.Ordinal) ? refName["refs/heads/".Length..]
        : refName;

    private static string? WebLink(JsonElement e)
    {
        if (Child(e, "_links") is { } links && Child(links, "web") is { } web)
            return Str(web, "href");
        return null;
    }
}
