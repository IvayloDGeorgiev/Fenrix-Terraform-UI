namespace Fenrix.IaCStudio.Domain.Git;

/// <summary>
/// The per-side state of a path in <c>git status --porcelain=v2</c>. Each changed entry carries two of
/// these — one for the index (staged) side and one for the working-tree (unstaged) side. See
/// docs/08-git-engine.md.
/// </summary>
public enum GitChangeState
{
    /// <summary>No change on this side (porcelain '.').</summary>
    Unmodified = 0,

    /// <summary>Content modified (porcelain 'M').</summary>
    Modified = 1,

    /// <summary>Newly added / tracked (porcelain 'A').</summary>
    Added = 2,

    /// <summary>Removed (porcelain 'D').</summary>
    Deleted = 3,

    /// <summary>Renamed (porcelain 'R'); carries an original path.</summary>
    Renamed = 4,

    /// <summary>Copied (porcelain 'C'); carries an original path.</summary>
    Copied = 5,

    /// <summary>File type changed, e.g. file ↔ symlink (porcelain 'T').</summary>
    TypeChanged = 6,

    /// <summary>Unmerged / conflicted (porcelain 'U').</summary>
    Unmerged = 7,

    /// <summary>Not tracked by git (a '?' record).</summary>
    Untracked = 8,

    /// <summary>Ignored by git (a '!' record).</summary>
    Ignored = 9
}
