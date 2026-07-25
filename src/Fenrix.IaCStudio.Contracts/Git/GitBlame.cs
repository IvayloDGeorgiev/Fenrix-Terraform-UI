namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// One line of <c>git blame --line-porcelain</c> output: the final line number, the commit that last touched
/// it, the author, when, the commit summary, and the line content. Consecutive lines from the same commit
/// are grouped by the UI into blame "hunks" for a clean gutter. See docs/08-git-engine.md.
/// </summary>
public sealed record GitBlameLine(
    int LineNumber,
    string Sha,
    string ShortSha,
    string Author,
    DateTimeOffset Date,
    string Summary,
    string Content,
    bool IsBoundary)
{
    /// <summary>An uncommitted (working-tree) line — Git reports the all-zero "not committed yet" SHA.</summary>
    public bool IsUncommitted => Sha.Length > 0 && Sha.All(c => c == '0');
}

/// <summary>The blame for a single file: its path and the per-line attributions in file order.</summary>
public sealed record GitBlame(string Path, IReadOnlyList<GitBlameLine> Lines)
{
    public static GitBlame Empty(string path) => new(path, []);
}
