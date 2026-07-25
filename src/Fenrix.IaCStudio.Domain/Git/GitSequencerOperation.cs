namespace Fenrix.IaCStudio.Domain.Git;

/// <summary>
/// A multi-step Git operation that can pause mid-way (typically on a conflict) and expose continue / abort /
/// skip controls. Detected from the marker files Git writes under the git directory (<c>MERGE_HEAD</c>,
/// <c>CHERRY_PICK_HEAD</c>, <c>REVERT_HEAD</c>, <c>rebase-merge/</c>, <c>rebase-apply/</c>). See
/// docs/08-git-engine.md (Advanced + Safety).
/// </summary>
public enum GitSequencerOperation
{
    /// <summary>No in-progress operation; the working tree is at rest.</summary>
    None = 0,

    /// <summary>A merge is in progress (<c>MERGE_HEAD</c> present).</summary>
    Merge = 1,

    /// <summary>A cherry-pick is in progress (<c>CHERRY_PICK_HEAD</c> present).</summary>
    CherryPick = 2,

    /// <summary>A revert is in progress (<c>REVERT_HEAD</c> present).</summary>
    Revert = 3,

    /// <summary>A rebase is in progress (<c>rebase-merge/</c> or <c>rebase-apply/</c> present).</summary>
    Rebase = 4
}
