using System.Globalization;
using Fenrix.IaCStudio.Contracts.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses <c>git for-each-ref --format=<see cref="GitCommandCatalog.TagFormat"/> refs/tags</c>: one tag per
/// line, NUL-delimited fields <c>short-name, objecttype, objectname, *objectname, creatordate, subject</c>.
/// <c>objecttype == "tag"</c> means an annotated tag (its <c>*objectname</c> dereferences to the target
/// commit); anything else is a lightweight tag pointing straight at a commit. See docs/08-git-engine.md.
/// </summary>
public static class GitTagParser
{
    public static IReadOnlyList<GitTag> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var tags = new List<GitTag>();
        foreach (var line in raw.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
                continue;

            var f = trimmed.Split('\0');
            if (f.Length < 3 || string.IsNullOrEmpty(f[0]))
                continue;

            var name = f[0];
            var objectType = f[1];
            var objectName = f[2];
            var derefName = f.Length > 3 ? f[3] : string.Empty;
            var date = f.Length > 4 ? ParseDate(f[4]) : default;
            var subject = f.Length > 5 ? f[5] : string.Empty;

            var annotated = objectType == "tag";
            var target = annotated && !string.IsNullOrEmpty(derefName) ? derefName : objectName;

            tags.Add(new GitTag(name, annotated, target, date, string.IsNullOrEmpty(subject) ? null : subject));
        }
        return tags;
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d
            : default;
}
