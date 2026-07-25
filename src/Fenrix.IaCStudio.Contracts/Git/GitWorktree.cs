namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// One entry from <c>git worktree list --porcelain</c>: the checkout path, the commit it is on, the branch
/// (null when detached), and whether it is the main worktree, bare, or locked. See docs/08-git-engine.md.
/// </summary>
public sealed record GitWorktree(
    string Path,
    string? Head,
    string? Branch,
    bool IsBare,
    bool IsDetached,
    bool IsLocked,
    bool IsMain);
