using Fenrix.IaCStudio.Contracts.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Parses a unified diff (from <c>git diff</c> / <c>git show -p</c>, <c>--no-color</c>) into per-file hunks
/// with computed old/new line numbers, for the read-only diff viewer. Handles renames, binary files, and
/// "No newline at end of file" markers. A single call may contain several files. See docs/08-git-engine.md.
/// </summary>
public static class GitDiffParser
{
    public static IReadOnlyList<GitDiffFile> Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        var files = new List<GitDiffFile>();
        var lines = raw.Replace("\r\n", "\n").Split('\n');

        // Per-file accumulators.
        string path = string.Empty; string? oldPath = null;
        var isBinary = false; var isRename = false;
        var added = 0; var deleted = 0;
        var hunks = new List<GitDiffHunk>();
        List<GitDiffLine>? hunkLines = null;
        string hunkHeader = string.Empty;
        var oldLine = 0; var newLine = 0;
        var open = false;

        void FlushHunk()
        {
            if (hunkLines is not null)
                hunks.Add(new GitDiffHunk(hunkHeader, hunkLines));
            hunkLines = null;
        }

        void FlushFile()
        {
            if (!open) return;
            FlushHunk();
            files.Add(new GitDiffFile(path, oldPath, isBinary, isRename, added, deleted, hunks));
            open = false;
        }

        foreach (var line in lines)
        {
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                FlushFile();
                (oldPath, path) = ParseDiffGitHeader(line);
                isBinary = false; isRename = false; added = 0; deleted = 0;
                hunks = []; hunkLines = null; hunkHeader = string.Empty; oldLine = 0; newLine = 0;
                open = true;
                continue;
            }
            if (!open)
                continue;

            if (line.StartsWith("rename from ", StringComparison.Ordinal))
            {
                oldPath = line["rename from ".Length..]; isRename = true; continue;
            }
            if (line.StartsWith("rename to ", StringComparison.Ordinal))
            {
                path = line["rename to ".Length..]; isRename = true; continue;
            }
            if (line.StartsWith("--- ", StringComparison.Ordinal))
            {
                var p = line[4..];
                if (p != "/dev/null") oldPath = StripPrefix(p);
                continue;
            }
            if (line.StartsWith("+++ ", StringComparison.Ordinal))
            {
                var p = line[4..];
                if (p != "/dev/null") path = StripPrefix(p);
                continue;
            }
            if (line.StartsWith("Binary files ", StringComparison.Ordinal) ||
                line.StartsWith("GIT binary patch", StringComparison.Ordinal))
            {
                isBinary = true; continue;
            }
            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                FlushHunk();
                hunkLines = [];
                hunkHeader = line;
                (oldLine, newLine) = ParseHunkHeader(line);
                hunkLines.Add(new GitDiffLine(GitDiffLineKind.Hunk, line, null, null));
                continue;
            }
            if (hunkLines is null)
                continue; // "index …", "new file mode …", "old mode …", etc.

            if (line.StartsWith('+'))
            {
                added++;
                hunkLines.Add(new GitDiffLine(GitDiffLineKind.Added, line[1..], null, newLine));
                newLine++;
            }
            else if (line.StartsWith('-'))
            {
                deleted++;
                hunkLines.Add(new GitDiffLine(GitDiffLineKind.Removed, line[1..], oldLine, null));
                oldLine++;
            }
            else if (line.StartsWith('\\'))
            {
                // "\ No newline at end of file" — attach to the viewer but count nothing.
                hunkLines.Add(new GitDiffLine(GitDiffLineKind.Context, line, null, null));
            }
            else
            {
                var text = line.Length > 0 ? line[1..] : string.Empty; // leading space
                hunkLines.Add(new GitDiffLine(GitDiffLineKind.Context, text, oldLine, newLine));
                oldLine++; newLine++;
            }
        }

        FlushFile();
        return files;
    }

    /// <summary>Builds an all-added diff for an untracked file read from disk (git emits no diff for these).</summary>
    public static GitDiffFile FromUntracked(string relativePath, string content)
    {
        var contentLines = content.Replace("\r\n", "\n").Split('\n');
        // A trailing newline yields a final empty element — drop it so counts match the file.
        var count = contentLines.Length;
        if (count > 0 && contentLines[^1].Length == 0) count--;

        var lines = new List<GitDiffLine>(count + 1)
        {
            new(GitDiffLineKind.Hunk, $"@@ -0,0 +1,{count} @@", null, null)
        };
        for (var i = 0; i < count; i++)
            lines.Add(new GitDiffLine(GitDiffLineKind.Added, contentLines[i], null, i + 1));

        var hunk = new GitDiffHunk($"@@ -0,0 +1,{count} @@", lines);
        return new GitDiffFile(relativePath, null, IsBinary: false, IsRename: false, Added: count, Deleted: 0, [hunk]);
    }

    private static (string? Old, string New) ParseDiffGitHeader(string line)
    {
        // "diff --git a/old path b/new path" — ambiguous with spaces, but the --- / +++ lines refine it.
        var body = line["diff --git ".Length..];
        var aIdx = body.IndexOf("a/", StringComparison.Ordinal);
        var bIdx = body.IndexOf(" b/", StringComparison.Ordinal);
        if (aIdx == 0 && bIdx > 0)
        {
            var old = body[2..bIdx];
            var neu = body[(bIdx + 3)..];
            return (old, neu);
        }
        return (null, body);
    }

    private static string StripPrefix(string p) =>
        p.StartsWith("a/", StringComparison.Ordinal) || p.StartsWith("b/", StringComparison.Ordinal) ? p[2..] : p;

    // "@@ -oldStart,oldCount +newStart,newCount @@ optional section"
    private static (int Old, int New) ParseHunkHeader(string header)
    {
        var oldStart = 0; var newStart = 0;
        var at = header.IndexOf("@@", StringComparison.Ordinal);
        var end = header.IndexOf("@@", at + 2, StringComparison.Ordinal);
        var mid = end > at ? header[(at + 2)..end].Trim() : header;
        foreach (var part in mid.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith('-') && TryStart(part.AsSpan(1), out oldStart)) continue;
            if (part.StartsWith('+') && TryStart(part.AsSpan(1), out newStart)) continue;
        }
        return (oldStart, newStart);

        static bool TryStart(ReadOnlySpan<char> span, out int start)
        {
            var comma = span.IndexOf(',');
            var num = comma >= 0 ? span[..comma] : span;
            return int.TryParse(num, out start);
        }
    }
}
