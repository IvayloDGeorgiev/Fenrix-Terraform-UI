namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>What to do with a commit during an interactive rebase, mirroring the todo-list verbs.</summary>
public enum RebaseAction
{
    /// <summary>Keep the commit as-is.</summary>
    Pick = 0,

    /// <summary>Keep the commit but replace its message (<see cref="GitRebaseStep.NewMessage"/>).</summary>
    Reword = 1,

    /// <summary>Stop after applying so the working tree can be amended, then continue.</summary>
    Edit = 2,

    /// <summary>Meld into the previous commit, combining messages.</summary>
    Squash = 3,

    /// <summary>Meld into the previous commit, discarding this message.</summary>
    Fixup = 4,

    /// <summary>Remove the commit entirely.</summary>
    Drop = 5
}

/// <summary>
/// One step in an interactive-rebase plan: the commit, its display fields, the chosen action, and (for
/// reword) the replacement message. Order in the plan is the order the commits will be applied. See
/// docs/08-git-engine.md.
/// </summary>
public sealed record GitRebaseStep(
    string Sha,
    string ShortSha,
    string Subject,
    RebaseAction Action,
    string? NewMessage = null);

/// <summary>
/// A complete interactive-rebase request: the base the range is replayed onto (e.g. <c>HEAD~5</c>), the
/// ordered steps, and whether to let Git auto-order <c>fixup!/squash!</c> commits instead of using the
/// explicit step list. Fenrix drives this non-interactively via <c>GIT_SEQUENCE_EDITOR</c>/<c>GIT_EDITOR</c>.
/// See docs/08-git-engine.md.
/// </summary>
public sealed record GitRebasePlan(
    string Base,
    IReadOnlyList<GitRebaseStep> Steps,
    bool Autosquash = false);
