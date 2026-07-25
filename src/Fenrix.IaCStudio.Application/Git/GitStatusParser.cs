using Fenrix.IaCStudio.Contracts.Git;
using Fenrix.IaCStudio.Domain.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses <c>git status --porcelain=v2 -z --branch</c> into a <see cref="GitStatus"/>. Porcelain v2 is
/// stable across git configuration; <c>-z</c> makes every record NUL-terminated so paths with spaces or
/// unusual characters need no unquoting. Rename/copy records (type '2') carry the original path as a
/// <em>separate</em> NUL-terminated token that immediately follows the change record. See
/// docs/08-git-engine.md.
/// </summary>
public static class GitStatusParser
{
    public static GitStatus Parse(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return new GitStatus(true, null, null, null, 0, 0, false, []);

        // Split on NUL. Rename records consume the following token, so walk with an index.
        var tokens = raw.Split('\0');
        string? branch = null, oid = null, upstream = null;
        var ahead = 0; var behind = 0; var detached = false;
        var entries = new List<GitStatusEntry>();

        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];
            if (token.Length == 0)
                continue;

            switch (token[0])
            {
                case '#':
                    ParseHeader(token, ref branch, ref oid, ref upstream, ref ahead, ref behind, ref detached);
                    break;
                case '1':
                    entries.Add(ParseOrdinary(token));
                    break;
                case '2':
                    // The original path is the next NUL-terminated token.
                    var original = i + 1 < tokens.Length ? tokens[++i] : null;
                    entries.Add(ParseRename(token, original));
                    break;
                case 'u':
                    entries.Add(ParseUnmerged(token));
                    break;
                case '?':
                    entries.Add(Simple(token[2..], GitChangeState.Untracked, untracked: true));
                    break;
                case '!':
                    entries.Add(Simple(token[2..], GitChangeState.Ignored, ignored: true));
                    break;
            }
        }

        return new GitStatus(true, branch, oid, upstream, ahead, behind, detached, entries);
    }

    private static void ParseHeader(
        string token, ref string? branch, ref string? oid, ref string? upstream,
        ref int ahead, ref int behind, ref bool detached)
    {
        // "# branch.oid <sha>" / "# branch.head <name>" / "# branch.upstream <name>" / "# branch.ab +N -M"
        const string oidPrefix = "# branch.oid ";
        const string headPrefix = "# branch.head ";
        const string upstreamPrefix = "# branch.upstream ";
        const string abPrefix = "# branch.ab ";

        if (token.StartsWith(oidPrefix, StringComparison.Ordinal))
        {
            var v = token[oidPrefix.Length..];
            oid = v == "(initial)" ? null : v;
        }
        else if (token.StartsWith(headPrefix, StringComparison.Ordinal))
        {
            var v = token[headPrefix.Length..];
            if (v == "(detached)") { detached = true; branch = null; }
            else branch = v;
        }
        else if (token.StartsWith(upstreamPrefix, StringComparison.Ordinal))
        {
            upstream = token[upstreamPrefix.Length..];
        }
        else if (token.StartsWith(abPrefix, StringComparison.Ordinal))
        {
            foreach (var part in token[abPrefix.Length..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (part.Length < 2) continue;
                if (int.TryParse(part.AsSpan(1), out var n))
                {
                    if (part[0] == '+') ahead = n;
                    else if (part[0] == '-') behind = n;
                }
            }
        }
    }

    // 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>
    private static GitStatusEntry ParseOrdinary(string token)
    {
        var parts = token.Split(' ', 9);
        var (index, work) = ParseXy(Field(parts, 1));
        var path = parts.Length > 8 ? parts[8] : string.Empty;
        return new GitStatusEntry(path, null, index, work, IsConflicted: false, IsUntracked: false, IsIgnored: false, null);
    }

    // 2 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <X score> <path>   (+ original path as the next token)
    private static GitStatusEntry ParseRename(string token, string? originalPath)
    {
        var parts = token.Split(' ', 10);
        var (index, work) = ParseXy(Field(parts, 1));
        var score = ParseScore(Field(parts, 8));
        var path = parts.Length > 9 ? parts[9] : string.Empty;
        return new GitStatusEntry(path, originalPath, index, work, IsConflicted: false, IsUntracked: false, IsIgnored: false, score);
    }

    // u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>
    private static GitStatusEntry ParseUnmerged(string token)
    {
        var parts = token.Split(' ', 11);
        var (index, work) = ParseXy(Field(parts, 1));
        var path = parts.Length > 10 ? parts[10] : string.Empty;
        return new GitStatusEntry(path, null, index, work, IsConflicted: true, IsUntracked: false, IsIgnored: false, null);
    }

    private static GitStatusEntry Simple(string path, GitChangeState state, bool untracked = false, bool ignored = false) =>
        new(path, null, GitChangeState.Unmodified, state, IsConflicted: false, IsUntracked: untracked, IsIgnored: ignored, null);

    private static (GitChangeState Index, GitChangeState Work) ParseXy(string xy)
    {
        if (xy.Length < 2)
            return (GitChangeState.Unmodified, GitChangeState.Unmodified);
        return (FromLetter(xy[0]), FromLetter(xy[1]));
    }

    private static GitChangeState FromLetter(char c) => c switch
    {
        'M' => GitChangeState.Modified,
        'A' => GitChangeState.Added,
        'D' => GitChangeState.Deleted,
        'R' => GitChangeState.Renamed,
        'C' => GitChangeState.Copied,
        'T' => GitChangeState.TypeChanged,
        'U' => GitChangeState.Unmerged,
        _ => GitChangeState.Unmodified
    };

    private static int? ParseScore(string field)
    {
        // e.g. "R100" or "C75" → 100 / 75.
        if (field.Length < 2) return null;
        return int.TryParse(field.AsSpan(1), out var n) ? n : null;
    }

    private static string Field(string[] parts, int i) => i < parts.Length ? parts[i] : string.Empty;
}
