using System.Globalization;
using Fenrix.IaCStudio.Contracts.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses <c>git reflog --format=<see cref="GitCommandCatalog.ReflogFormat"/></c>: one entry per record
/// (0x1e terminated) with NUL-delimited fields <c>sha, shortSha, selector, subject, author, iso-date</c>.
/// The reflog subject is <c>&lt;action&gt;: &lt;description&gt;</c> (e.g. <c>commit: fix</c>,
/// <c>reset: moving to HEAD~1</c>); records without a colon are treated as a bare action. See
/// docs/08-git-engine.md.
/// </summary>
public static class GitReflogParser
{
    private const char RecordSeparator = '\u001e';

    public static IReadOnlyList<GitReflogEntry> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var entries = new List<GitReflogEntry>();
        foreach (var record in raw.Split(RecordSeparator))
        {
            var trimmed = record.Trim('\n', '\r');
            if (trimmed.Length == 0)
                continue;

            var f = trimmed.Split('\0');
            if (f.Length < 4)
                continue;

            var sha = f[0];
            var shortSha = f[1];
            var selector = f[2];
            var subject = f[3];
            var author = f.Length > 4 ? f[4] : string.Empty;
            var date = f.Length > 5 ? ParseDate(f[5]) : default;

            var (action, description) = SplitSubject(subject);
            entries.Add(new GitReflogEntry(selector, sha, shortSha, action, description, author, date));
        }
        return entries;
    }

    // "commit: message" → ("commit", "message"); a bare subject with no colon becomes the action.
    private static (string Action, string Description) SplitSubject(string subject)
    {
        var colon = subject.IndexOf(':');
        if (colon < 0)
            return (subject.Trim(), string.Empty);
        return (subject[..colon].Trim(), subject[(colon + 1)..].Trim());
    }

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d
            : default;
}
