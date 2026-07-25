using Fenrix.IaCStudio.Contracts.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses <c>git worktree list --porcelain</c>: blank-line-separated records of attribute lines
/// (<c>worktree &lt;path&gt;</c>, <c>HEAD &lt;sha&gt;</c>, <c>branch &lt;ref&gt;</c>, and the bare flags
/// <c>bare</c> / <c>detached</c> / <c>locked</c>). The first record is the main worktree. See
/// docs/08-git-engine.md.
/// </summary>
public static class GitWorktreeParser
{
    public static IReadOnlyList<GitWorktree> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var worktrees = new List<GitWorktree>();
        var text = raw.Replace("\r\n", "\n");

        string? path = null, head = null, branch = null;
        bool bare = false, detached = false, locked = false;
        var first = true;

        void Flush()
        {
            if (path is null) return;
            worktrees.Add(new GitWorktree(path, head, ShortBranch(branch), bare, detached, locked, first));
            first = false;
            path = null; head = null; branch = null; bare = false; detached = false; locked = false;
        }

        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0) { Flush(); continue; }

            var space = line.IndexOf(' ');
            var key = space < 0 ? line : line[..space];
            var value = space < 0 ? string.Empty : line[(space + 1)..];
            switch (key)
            {
                case "worktree": Flush(); path = value; break;
                case "HEAD": head = value; break;
                case "branch": branch = value; break;
                case "bare": bare = true; break;
                case "detached": detached = true; break;
                case "locked": locked = true; break;
            }
        }
        Flush();
        return worktrees;
    }

    // refs/heads/feature → feature
    private static string? ShortBranch(string? refName)
    {
        if (string.IsNullOrEmpty(refName)) return null;
        const string prefix = "refs/heads/";
        return refName.StartsWith(prefix, StringComparison.Ordinal) ? refName[prefix.Length..] : refName;
    }
}
