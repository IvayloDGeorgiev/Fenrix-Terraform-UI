namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>Which side a diff is taken from.</summary>
public enum GitDiffSource
{
    /// <summary>Unstaged working-tree changes (<c>git diff</c>).</summary>
    WorkTree = 0,

    /// <summary>Staged changes vs HEAD (<c>git diff --cached</c>).</summary>
    Staged = 1,

    /// <summary>A specific commit's patch (<c>git show</c>).</summary>
    Commit = 2,

    /// <summary>An untracked file rendered as an all-added diff (no git command; read from disk).</summary>
    Untracked = 3
}

/// <summary>
/// What to diff: the source side, an optional commit, and optional path filter. Shared by the command
/// catalog (to build the exact <c>git</c> invocation) and the UI. See docs/08-git-engine.md.
/// </summary>
public sealed record GitDiffSpec(
    GitDiffSource Source,
    string? CommitSha = null,
    string? Path = null);

/// <summary>The role of a single line in a unified diff.</summary>
public enum GitDiffLineKind
{
    Context = 0,
    Added = 1,
    Removed = 2,
    /// <summary>A hunk header line (<c>@@ … @@</c>).</summary>
    Hunk = 3
}

/// <summary>
/// One line of a unified diff with its computed old/new line numbers (null on the side where the line does
/// not exist). See docs/08-git-engine.md.
/// </summary>
public sealed record GitDiffLine(GitDiffLineKind Kind, string Text, int? OldLine, int? NewLine);

/// <summary>A contiguous change block within a file diff.</summary>
public sealed record GitDiffHunk(string Header, IReadOnlyList<GitDiffLine> Lines);

/// <summary>
/// The diff for a single file: its path (and original path for renames), binary flag, added/removed line
/// counts, and the parsed hunks for the read-only viewer. See docs/08-git-engine.md.
/// </summary>
public sealed record GitDiffFile(
    string Path,
    string? OldPath,
    bool IsBinary,
    bool IsRename,
    int Added,
    int Deleted,
    IReadOnlyList<GitDiffHunk> Hunks)
{
    public static GitDiffFile Empty { get; } = new(string.Empty, null, false, false, 0, 0, []);
}
