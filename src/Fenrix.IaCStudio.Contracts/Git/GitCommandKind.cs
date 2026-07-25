namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// The Git operations Fenrix drives in Phase 5. Each maps to exactly one <c>git</c> subcommand invocation
/// built by the command catalog, so the preview and the execution share one argument list. See
/// docs/08-git-engine.md and docs/23-command-transparency.md.
/// </summary>
public enum GitCommandKind
{
    // Repository
    Version,
    Init,
    Clone,
    RevParseTopLevel,

    // Working tree
    Status,
    StageAll,
    Stage,
    UnstageAll,
    Unstage,
    Discard,
    Commit,

    // Remotes (local-only posture this phase — run non-interactively)
    Fetch,
    Pull,
    Push,

    // Branches
    BranchList,
    BranchCreate,
    Checkout,
    BranchRename,
    BranchDelete,
    SetUpstream,
    Merge,
    MergeAbort,

    // History & inspection
    Log,
    CommitDetail,
    Diff,

    // Stash
    StashList,
    StashPush,
    StashApply,
    StashPop,
    StashDrop,

    // --- Phase 6: advanced ---

    // Inspection & local history rewriting
    Reflog,
    Blame,
    Reset,
    CherryPick,
    Revert,
    Sequencer,      // --continue / --abort / --skip / --quit for cherry-pick/revert/rebase
    CommitGraph,
    RevParseGitDir,

    // Tags
    TagList,
    TagCreate,
    TagDelete,
    TagPush,
    TagPushAll,
    TagDeleteRemote,

    // Worktrees
    WorktreeList,
    WorktreeAdd,
    WorktreeRemove,
    WorktreePrune,

    // Submodules
    SubmoduleStatus,
    SubmoduleUpdate,
    SubmoduleSync,
    SubmoduleAdd,

    // Git LFS
    LfsStatus,
    LfsTrack,

    // Interactive rebase
    Rebase,
    RebaseInteractive,

    // Partial / line staging
    ApplyPatch,

    // Conflict editor
    Checkout2,      // checkout --ours/--theirs for a conflicted path
    ShowObject      // show a blob at a stage or revision (base/ours/theirs content)
}
