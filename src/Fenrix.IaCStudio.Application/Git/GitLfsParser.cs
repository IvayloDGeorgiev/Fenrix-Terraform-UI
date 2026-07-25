namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses <c>git lfs track</c> output — a header line followed by indented
/// <c>&lt;pattern&gt; (&lt;source&gt;)</c> lines — into the set of tracked path patterns. See
/// docs/08-git-engine.md.
/// </summary>
public static class GitLfsParser
{
    public static IReadOnlyList<string> ParseTrackedPatterns(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var patterns = new List<string>();
        foreach (var line in raw.Replace("\r\n", "\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                continue;
            // Skip the "Listing tracked patterns …" header (no leading indent in the raw line).
            if (!line.StartsWith(" ") && !line.StartsWith("\t"))
                continue;

            var paren = trimmed.LastIndexOf(" (", StringComparison.Ordinal);
            var pattern = paren >= 0 ? trimmed[..paren].Trim() : trimmed;
            if (pattern.Length > 0)
                patterns.Add(pattern);
        }
        return patterns;
    }
}
