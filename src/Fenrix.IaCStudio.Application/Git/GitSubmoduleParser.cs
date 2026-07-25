using Fenrix.IaCStudio.Contracts.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses <c>git submodule status</c>: each line is <c>&lt;marker&gt;&lt;sha&gt; &lt;path&gt; [(&lt;describe&gt;)]</c>
/// where the leading marker is <c>' '</c> in-sync, <c>'-'</c> not initialised, <c>'+'</c> at a different
/// commit, or <c>'U'</c> conflicted. See docs/08-git-engine.md.
/// </summary>
public static class GitSubmoduleParser
{
    public static IReadOnlyList<GitSubmodule> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var modules = new List<GitSubmodule>();
        foreach (var line in raw.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Length < 2)
                continue;

            var marker = line[0];
            var state = marker switch
            {
                '-' => GitSubmoduleState.Uninitialised,
                '+' => GitSubmoduleState.OutOfSync,
                'U' => GitSubmoduleState.Conflicted,
                _ => GitSubmoduleState.InSync
            };

            var rest = line[1..];
            var firstSpace = rest.IndexOf(' ');
            if (firstSpace < 0)
                continue;

            var sha = rest[..firstSpace];
            var after = rest[(firstSpace + 1)..].TrimStart();

            string path;
            string? describe = null;
            var paren = after.LastIndexOf('(');
            if (paren >= 0 && after.EndsWith(')'))
            {
                path = after[..paren].TrimEnd();
                describe = after[(paren + 1)..^1];
            }
            else
            {
                path = after.TrimEnd();
            }

            modules.Add(new GitSubmodule(path, sha, state, describe));
        }
        return modules;
    }
}
