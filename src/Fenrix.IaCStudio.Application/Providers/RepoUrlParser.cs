using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Application.Providers;

/// <summary>
/// Derives a host-specific repository identifier from a Git remote URL (HTTPS or SSH), so the provider
/// adapters can address the exact repo a project maps to for PR/pipeline/branch-policy calls. Handles the
/// scp-like SSH form (<c>git@host:owner/repo.git</c>) and standard URLs. Pure logic (no I/O). See
/// docs/09-provider-integrations.md.
/// </summary>
public static class RepoUrlParser
{
    /// <summary>
    /// The repository id an adapter expects: <c>owner/repo</c> for GitHub, <c>workspace/repo</c> for
    /// Bitbucket, the URL-encodable <c>group/…/project</c> path for GitLab, and the repo name for Azure
    /// DevOps. Null when it can't be derived (or the provider has no host API).
    /// </summary>
    public static string? RepoId(RepositoryProviderType provider, string? remoteUrl)
    {
        var segments = PathSegments(remoteUrl);
        if (segments.Count == 0)
            return null;

        return provider switch
        {
            RepositoryProviderType.GitHub => Join(Last(segments, 2)),
            RepositoryProviderType.Bitbucket => Join(Last(segments, 2)),
            RepositoryProviderType.GitLab => Join(segments),          // path-with-namespace (subgroups allowed)
            RepositoryProviderType.AzureDevOps => AzureRepo(segments),
            _ => null
        };
    }

    /// <summary>The bare owner/namespace of the remote (first path segment), for display.</summary>
    public static string? Owner(string? remoteUrl)
    {
        var segments = PathSegments(remoteUrl);
        return segments.Count > 0 ? segments[0] : null;
    }

    /// <summary>The repo name (last path segment, minus <c>.git</c>), for display.</summary>
    public static string? RepoName(string? remoteUrl)
    {
        var segments = PathSegments(remoteUrl);
        return segments.Count > 0 ? segments[^1] : null;
    }

    private static string AzureRepo(List<string> segments)
    {
        // dev.azure.com/{org}/{project}/_git/{repo}  or  {org}@vs-ssh…:v3/{org}/{project}/{repo}
        var gitIdx = segments.FindIndex(s => s.Equals("_git", StringComparison.OrdinalIgnoreCase));
        return gitIdx >= 0 && gitIdx + 1 < segments.Count ? segments[gitIdx + 1] : segments[^1];
    }

    private static List<string> PathSegments(string? remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
            return [];

        var url = remoteUrl.Trim();
        string path;

        // scp-like SSH: git@host:owner/repo.git  (no scheme, single ':' before the path)
        if (!url.Contains("://") && url.Contains(':'))
        {
            path = url[(url.IndexOf(':') + 1)..];
        }
        else
        {
            var withoutScheme = url.Contains("://") ? url[(url.IndexOf("://", StringComparison.Ordinal) + 3)..] : url;
            var slash = withoutScheme.IndexOf('/');
            path = slash >= 0 ? withoutScheme[slash..] : string.Empty;
        }

        path = path.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            path = path[..^4];

        return path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static List<string> Last(List<string> segments, int count) =>
        segments.Count <= count ? segments : segments.GetRange(segments.Count - count, count);

    private static string Join(List<string> segments) => string.Join('/', segments);
}
