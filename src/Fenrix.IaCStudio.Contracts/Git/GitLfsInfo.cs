namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// Git LFS presence for a repository: whether the <c>git-lfs</c> extension is installed, and the set of
/// path patterns tracked by LFS (from <c>.gitattributes</c> via <c>git lfs track</c>). Used only for
/// non-intrusive indicators — Fenrix does not manage LFS objects itself. See docs/08-git-engine.md.
/// </summary>
public sealed record GitLfsInfo(
    bool IsInstalled,
    bool IsEnabled,
    IReadOnlyList<string> TrackedPatterns)
{
    public static GitLfsInfo NotInstalled { get; } = new(false, false, []);

    /// <summary>True when a path matches one of the tracked LFS glob patterns (simple suffix/glob match).</summary>
    public bool IsTracked(string path)
    {
        if (!IsEnabled || TrackedPatterns.Count == 0 || string.IsNullOrEmpty(path))
            return false;
        var name = path.Replace('\\', '/');
        foreach (var pattern in TrackedPatterns)
        {
            var p = pattern.Trim();
            if (p.Length == 0) continue;
            if (p.StartsWith("*.", StringComparison.Ordinal))
            {
                if (name.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase)) return true;
            }
            else if (name.EndsWith(p, StringComparison.OrdinalIgnoreCase) ||
                     name.Equals(p, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
