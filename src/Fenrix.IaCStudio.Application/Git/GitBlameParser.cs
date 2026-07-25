using System.Globalization;
using Fenrix.IaCStudio.Contracts.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses <c>git blame --line-porcelain</c>. Each line is a record that starts with a header
/// <c>&lt;sha&gt; &lt;orig-line&gt; &lt;final-line&gt; [&lt;group-size&gt;]</c>, followed by repeated
/// key/value metadata lines (<c>author</c>, <c>author-time</c>, <c>summary</c>, <c>boundary</c>, …), then a
/// single content line prefixed with a TAB. <c>--line-porcelain</c> repeats the metadata for every line, so
/// (unlike plain <c>--porcelain</c>) no commit cache is needed. See docs/08-git-engine.md.
/// </summary>
public static class GitBlameParser
{
    public static GitBlame Parse(string path, string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return GitBlame.Empty(path);

        var lines = new List<GitBlameLine>();
        var text = raw.Replace("\r\n", "\n");
        var rows = text.Split('\n');

        string sha = "", author = "", summary = "";
        long authorTime = 0;
        int authorTz = 0;
        int finalLine = 0;
        var boundary = false;
        var haveHeader = false;

        foreach (var row in rows)
        {
            if (row.Length == 0)
                continue;

            if (row[0] == '\t')
            {
                // Content line closes the current record.
                if (haveHeader)
                {
                    lines.Add(new GitBlameLine(
                        finalLine,
                        sha,
                        sha.Length >= 8 ? sha[..8] : sha,
                        author,
                        FromUnix(authorTime, authorTz),
                        summary,
                        row[1..],
                        boundary));
                }
                haveHeader = false;
                author = ""; summary = ""; boundary = false; authorTime = 0; authorTz = 0;
                continue;
            }

            if (IsHeader(row, out var hsha, out var hfinal))
            {
                sha = hsha;
                finalLine = hfinal;
                haveHeader = true;
                continue;
            }

            var space = row.IndexOf(' ');
            var key = space < 0 ? row : row[..space];
            var value = space < 0 ? string.Empty : row[(space + 1)..];
            switch (key)
            {
                case "author": author = value; break;
                case "author-time": long.TryParse(value, out authorTime); break;
                case "author-tz": authorTz = ParseTz(value); break;
                case "summary": summary = value; break;
                case "boundary": boundary = true; break;
            }
        }

        return new GitBlame(path, lines);
    }

    // Header: "<40-hex-sha> <orig> <final> [<group>]"
    private static bool IsHeader(string row, out string sha, out int finalLine)
    {
        sha = ""; finalLine = 0;
        var parts = row.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
            return false;
        if (parts[0].Length < 7 || !IsHex(parts[0]))
            return false;
        if (!int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out finalLine))
            return false;
        sha = parts[0];
        return true;
    }

    private static bool IsHex(string s)
    {
        foreach (var c in s)
            if (!Uri.IsHexDigit(c))
                return false;
        return true;
    }

    // "+0200" / "-0500" → total minutes offset.
    private static int ParseTz(string tz)
    {
        if (tz.Length < 5 || (tz[0] != '+' && tz[0] != '-'))
            return 0;
        if (!int.TryParse(tz.AsSpan(1, 2), out var h) || !int.TryParse(tz.AsSpan(3, 2), out var m))
            return 0;
        var mins = h * 60 + m;
        return tz[0] == '-' ? -mins : mins;
    }

    private static DateTimeOffset FromUnix(long seconds, int tzMinutes)
    {
        if (seconds <= 0)
            return default;
        var offset = TimeSpan.FromMinutes(tzMinutes);
        return new DateTimeOffset(DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime, TimeSpan.Zero).ToOffset(offset);
    }
}
