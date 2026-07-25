using Fenrix.IaCStudio.Contracts.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses <c>git branch --all --format=<see cref="GitCommandCatalog.BranchFormat"/></c> output: one ref per
/// line, fields NUL-delimited. Extracts current-branch marker, local vs remote-tracking, upstream and the
/// ahead/behind counts from the <c>[ahead N, behind M]</c> track string. The synthetic
/// <c>refs/remotes/&lt;remote&gt;/HEAD</c> pointer is skipped. See docs/08-git-engine.md.
/// </summary>
public static class GitBranchParser
{
    public static IReadOnlyList<GitBranch> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var branches = new List<GitBranch>();
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
                continue;

            var f = trimmed.Split('\0');
            if (f.Length < 7)
                continue;

            var full = f[1];
            var shortName = f[2];
            var isRemote = full.StartsWith("refs/remotes/", StringComparison.Ordinal);

            // Skip the remote symbolic HEAD (e.g. "origin/HEAD -> origin/main").
            if (isRemote && shortName.EndsWith("/HEAD", StringComparison.Ordinal))
                continue;

            var isCurrent = f[0] == "*";
            var upstream = string.IsNullOrEmpty(f[3]) ? null : f[3];
            var (ahead, behind) = ParseTrack(f[4]);
            var tip = string.IsNullOrEmpty(f[5]) ? null : f[5];
            var subject = f[6];

            branches.Add(new GitBranch(shortName, full, isCurrent, isRemote, upstream, ahead, behind, tip, subject));
        }
        return branches;
    }

    // "[ahead 2]", "[behind 3]", "[ahead 1, behind 4]", "[gone]", or "".
    private static (int Ahead, int Behind) ParseTrack(string track)
    {
        if (string.IsNullOrEmpty(track))
            return (0, 0);

        var inner = track.Trim('[', ']');
        var ahead = 0; var behind = 0;
        foreach (var part in inner.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("ahead ", StringComparison.Ordinal) && int.TryParse(part.AsSpan(6), out var a))
                ahead = a;
            else if (part.StartsWith("behind ", StringComparison.Ordinal) && int.TryParse(part.AsSpan(7), out var b))
                behind = b;
        }
        return (ahead, behind);
    }
}
