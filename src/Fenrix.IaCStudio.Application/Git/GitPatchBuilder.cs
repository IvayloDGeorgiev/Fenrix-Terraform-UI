using System.Globalization;
using System.Text;
using Fenrix.IaCStudio.Contracts.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Reconstructs a minimal unified patch containing only the user-selected changed lines of a single file, so
/// "stage selected lines" can be applied with <c>git apply --cached</c> (and unstaging with
/// <c>--reverse</c>). Unselected changes on the opposite side are downgraded to context so the surrounding
/// hunk still applies cleanly; hunk headers are recomputed for the trimmed line set. This is a pure function
/// with no I/O, cross-checked against real <c>git apply</c> in the Phase 6 verification. See
/// docs/08-git-engine.md.
/// </summary>
public static class GitPatchBuilder
{
    /// <summary>
    /// Builds a patch for the given file restricted to the selected changed lines. A line is identified by
    /// <c>(hunkIndex, lineIndex)</c> into <see cref="GitDiffFile.Hunks"/>. Returns null when nothing is
    /// selected (no-op). <paramref name="reverse"/> targets unstaging (the patch is meant for
    /// <c>git apply --cached --reverse</c>).
    /// </summary>
    public static string? Build(GitDiffFile file, ISet<(int Hunk, int Line)> selected, bool reverse)
    {
        if (file.IsBinary || file.Hunks.Count == 0 || selected.Count == 0)
            return null;

        var path = file.Path.Replace('\\', '/');
        var body = new StringBuilder();
        var running = 0;         // cumulative (newCount - oldCount) across emitted hunks
        var emittedAny = false;

        for (var h = 0; h < file.Hunks.Count; h++)
        {
            var hunk = file.Hunks[h];
            if (!ParseHeader(hunk.Header, out var oldStart, out _))
                continue;

            // Does this hunk contribute any selected change?
            var hasSelected = false;
            for (var i = 0; i < hunk.Lines.Count; i++)
            {
                var k = hunk.Lines[i].Kind;
                if ((k == GitDiffLineKind.Added || k == GitDiffLineKind.Removed) && selected.Contains((h, i)))
                {
                    hasSelected = true;
                    break;
                }
            }
            if (!hasSelected)
                continue;

            var lines = new List<string>();
            var oldCount = 0;
            var newCount = 0;

            for (var i = 0; i < hunk.Lines.Count; i++)
            {
                var line = hunk.Lines[i];
                switch (line.Kind)
                {
                    case GitDiffLineKind.Context:
                        lines.Add(" " + line.Text);
                        oldCount++; newCount++;
                        break;

                    case GitDiffLineKind.Added:
                        if (selected.Contains((h, i)))
                        {
                            lines.Add("+" + line.Text);
                            newCount++;
                        }
                        else if (reverse)
                        {
                            // Unstaging: an unselected addition already in the index must stay → context.
                            lines.Add(" " + line.Text);
                            oldCount++; newCount++;
                        }
                        // forward + unselected addition: omit
                        break;

                    case GitDiffLineKind.Removed:
                        if (selected.Contains((h, i)))
                        {
                            lines.Add("-" + line.Text);
                            oldCount++;
                        }
                        else if (!reverse)
                        {
                            // Staging: an unselected removal must remain in the index → context.
                            lines.Add(" " + line.Text);
                            oldCount++; newCount++;
                        }
                        // reverse + unselected removal: omit
                        break;
                }
            }

            var newStart = oldStart + running;
            body.Append("@@ -")
                .Append(oldStart).Append(',').Append(oldCount)
                .Append(" +")
                .Append(newStart).Append(',').Append(newCount)
                .Append(" @@\n");
            foreach (var l in lines)
                body.Append(l).Append('\n');

            running += newCount - oldCount;
            emittedAny = true;
        }

        if (!emittedAny)
            return null;

        var header = new StringBuilder()
            .Append("diff --git a/").Append(path).Append(" b/").Append(path).Append('\n')
            .Append("--- a/").Append(path).Append('\n')
            .Append("+++ b/").Append(path).Append('\n');

        return header.Append(body).ToString();
    }

    // "@@ -12,7 +12,6 @@ optional section heading" → oldStart / newStart.
    private static bool ParseHeader(string header, out int oldStart, out int newStart)
    {
        oldStart = 0; newStart = 0;
        var at = header.IndexOf("@@", StringComparison.Ordinal);
        if (at < 0) return false;
        var end = header.IndexOf("@@", at + 2, StringComparison.Ordinal);
        if (end < 0) return false;

        var inner = header.Substring(at + 2, end - at - 2).Trim();
        var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;

        return TryStart(parts[0], '-', out oldStart) && TryStart(parts[1], '+', out newStart);
    }

    private static bool TryStart(string token, char sign, out int start)
    {
        start = 0;
        if (token.Length == 0 || token[0] != sign) return false;
        var body = token[1..];
        var comma = body.IndexOf(',');
        var startText = comma >= 0 ? body[..comma] : body;
        return int.TryParse(startText, NumberStyles.Integer, CultureInfo.InvariantCulture, out start);
    }
}
