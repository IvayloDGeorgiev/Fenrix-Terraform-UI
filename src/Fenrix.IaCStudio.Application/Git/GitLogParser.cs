using System.Globalization;
using Fenrix.IaCStudio.Contracts.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses <c>git log --format=<see cref="GitCommandCatalog.LogFormat"/></c> output. Fields within a commit
/// are NUL-delimited and commits are terminated by the record separator 0x1e, so subjects and multi-line
/// bodies with arbitrary punctuation survive intact. See docs/08-git-engine.md.
/// </summary>
public static class GitLogParser
{
    private const char RecordSeparator = '\u001e';

    public static IReadOnlyList<GitCommit> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var commits = new List<GitCommit>();
        foreach (var record in raw.Split(RecordSeparator))
        {
            // Records after the first carry a leading newline (git separates them with "\x1e\n").
            var trimmed = record.Trim('\n', '\r');
            if (trimmed.Length == 0)
                continue;

            var f = trimmed.Split('\0');
            if (f.Length < 8)
                continue;

            var parents = f[5].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var date = ParseDate(f[4]);

            commits.Add(new GitCommit(
                Sha: f[0],
                ShortSha: f[1],
                Author: f[2],
                Email: f[3],
                Date: date,
                Parents: parents,
                Subject: f[6],
                Body: f[7].TrimEnd('\n', '\r')));
        }
        return commits;
    }

    private static DateTimeOffset ParseDate(string iso) =>
        DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d)
            ? d
            : DateTimeOffset.MinValue;
}
