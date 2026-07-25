namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// The three merge inputs and current working-tree content for a conflicted file, read from the index
/// stages: base = stage 1 (<c>:1:</c>, common ancestor), ours = stage 2 (<c>:2:</c>, current branch),
/// theirs = stage 3 (<c>:3:</c>, incoming). <see cref="Merged"/> is the on-disk file with conflict markers.
/// Any side may be null when the file was added/deleted on that side. See docs/08-git-engine.md.
/// </summary>
public sealed record GitConflictFile(
    string Path,
    string? Base,
    string? Ours,
    string? Theirs,
    string Merged)
{
    public bool HasBase => Base is not null;
}

/// <summary>Which side of a conflict to take wholesale, for the quick-resolve buttons.</summary>
public enum GitConflictSide
{
    Ours = 0,
    Theirs = 1
}
