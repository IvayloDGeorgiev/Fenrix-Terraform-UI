using Fenrix.IaCStudio.Contracts.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses <c>git stash list --format=<see cref="GitCommandCatalog.StashFormat"/></c> output: one stash per
/// line as <c>stash@{n}\0&lt;reflog subject&gt;</c>. The reflog subject is typically
/// <c>WIP on &lt;branch&gt;: &lt;sha&gt; &lt;subject&gt;</c> or <c>On &lt;branch&gt;: &lt;message&gt;</c>.
/// See docs/08-git-engine.md.
/// </summary>
public static class GitStashParser
{
    public static IReadOnlyList<GitStash> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var stashes = new List<GitStash>();
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
                continue;

            var f = trimmed.Split('\0');
            var selector = f[0];
            var subject = f.Length > 1 ? f[1] : string.Empty;

            var index = ParseIndex(selector);
            var (branch, message) = ParseSubject(subject);
            stashes.Add(new GitStash(index, selector, branch, message));
        }
        return stashes;
    }

    // "stash@{3}" → 3
    private static int ParseIndex(string selector)
    {
        var open = selector.IndexOf('{');
        var close = selector.IndexOf('}');
        if (open >= 0 && close > open && int.TryParse(selector.AsSpan(open + 1, close - open - 1), out var n))
            return n;
        return 0;
    }

    // "WIP on main: 1a2b3c subject" / "On main: message"
    private static (string? Branch, string Message) ParseSubject(string subject)
    {
        var colon = subject.IndexOf(':');
        if (colon < 0)
            return (null, subject);

        var head = subject[..colon];
        var message = subject[(colon + 1)..].Trim();

        string? branch = null;
        var onIdx = head.LastIndexOf(" on ", StringComparison.Ordinal);
        if (onIdx >= 0)
            branch = head[(onIdx + 4)..].Trim();
        else if (head.StartsWith("On ", StringComparison.Ordinal))
            branch = head[3..].Trim();

        return (string.IsNullOrEmpty(branch) ? null : branch, message);
    }
}
