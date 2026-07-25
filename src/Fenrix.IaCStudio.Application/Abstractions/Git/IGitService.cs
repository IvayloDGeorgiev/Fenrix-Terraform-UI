using Fenrix.IaCStudio.Contracts.Git;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Git;

namespace Fenrix.IaCStudio.Application.Abstractions.Git;

/// <summary>
/// The Phase 5 Git façade: repository detection/init/clone, working-tree status and staging, commit,
/// fetch/pull/push, branches, history, diff, stash, and merge with conflict detection. Every operation is
/// driven through the shared <c>ArgumentList</c> process runner via the command catalog, records a redacted
/// history row, and can be previewed with the exact command first (see <see cref="ResolveContextAsync"/>).
/// A local-only remote posture is used this phase: remote commands run non-interactively and surface auth
/// failures rather than prompting. See docs/08-git-engine.md and docs/23-command-transparency.md.
/// </summary>
public interface IGitService
{
    // ---- repository ----

    /// <summary>Detects whether a project's folder is inside a Git repository and reads HEAD/branch.</summary>
    Task<GitRepositoryInfo> DetectAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Initialises a repository at the project root.</summary>
    Task<GitOperationResult> InitAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Initialises a repository at an arbitrary directory (used during project creation).</summary>
    Task<GitOperationResult> InitAtAsync(string directory, CancellationToken ct = default);

    /// <summary>Clones a remote into <c>DestinationParent/FolderName</c>, streaming progress.</summary>
    Task<GitOperationResult> CloneAsync(GitCloneRequest request, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);

    /// <summary>Resolves the binary + repo working directory so the UI can build live command previews.</summary>
    Task<GitCommandContext?> ResolveContextAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Reads Git provenance (commit/branch/dirty) for a working directory, for saved-plan metadata.</summary>
    Task<GitProvenance> ReadProvenanceAsync(string workingDirectory, CancellationToken ct = default);

    // ---- working tree ----

    Task<GitStatus> GetStatusAsync(Guid projectId, CancellationToken ct = default);
    Task<GitOperationResult> StageAsync(Guid projectId, IReadOnlyList<string> paths, CancellationToken ct = default);
    Task<GitOperationResult> StageAllAsync(Guid projectId, CancellationToken ct = default);
    Task<GitOperationResult> UnstageAsync(Guid projectId, IReadOnlyList<string> paths, CancellationToken ct = default);
    Task<GitOperationResult> DiscardAsync(Guid projectId, IReadOnlyList<string> paths, bool untracked, CancellationToken ct = default);
    Task<GitOperationResult> CommitAsync(Guid projectId, GitCommitRequest request, CancellationToken ct = default);

    // ---- remotes (local-only posture) ----

    Task<GitOperationResult> FetchAsync(Guid projectId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);
    Task<GitOperationResult> PullAsync(Guid projectId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);
    Task<GitOperationResult> PushAsync(Guid projectId, bool forceWithLease, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);

    // ---- branches ----

    Task<IReadOnlyList<GitBranch>> GetBranchesAsync(Guid projectId, CancellationToken ct = default);
    Task<GitOperationResult> CreateBranchAsync(Guid projectId, string name, bool checkout, CancellationToken ct = default);
    Task<GitOperationResult> CheckoutAsync(Guid projectId, string name, CancellationToken ct = default);
    Task<GitOperationResult> RenameBranchAsync(Guid projectId, string oldName, string newName, CancellationToken ct = default);
    Task<GitOperationResult> DeleteBranchAsync(Guid projectId, string name, bool force, CancellationToken ct = default);
    Task<GitMergeResult> MergeAsync(Guid projectId, string branch, CancellationToken ct = default);
    Task<GitOperationResult> AbortMergeAsync(Guid projectId, CancellationToken ct = default);

    // ---- history & inspection ----

    Task<IReadOnlyList<GitCommit>> GetHistoryAsync(Guid projectId, int limit = 100, string? path = null, CancellationToken ct = default);
    Task<IReadOnlyList<GitDiffFile>> GetDiffAsync(Guid projectId, GitDiffSpec spec, CancellationToken ct = default);

    // ---- stash ----

    Task<IReadOnlyList<GitStash>> GetStashesAsync(Guid projectId, CancellationToken ct = default);
    Task<GitOperationResult> StashPushAsync(Guid projectId, string? message, bool includeUntracked, CancellationToken ct = default);
    Task<GitOperationResult> StashApplyAsync(Guid projectId, int index, CancellationToken ct = default);
    Task<GitOperationResult> StashPopAsync(Guid projectId, int index, CancellationToken ct = default);
    Task<GitOperationResult> StashDropAsync(Guid projectId, int index, CancellationToken ct = default);

    // ---- Phase 6: inspection & local history rewriting ----

    /// <summary>Reads the HEAD reflog — the safety net for recovering commits after a reset/rebase.</summary>
    Task<IReadOnlyList<GitReflogEntry>> GetReflogAsync(Guid projectId, int limit = 100, CancellationToken ct = default);

    /// <summary>Blames a file at an optional revision, one attribution per line.</summary>
    Task<GitBlame> GetBlameAsync(Guid projectId, string path, string? revision = null, CancellationToken ct = default);

    /// <summary>Detects an in-progress merge/cherry-pick/revert/rebase and any conflicted paths.</summary>
    Task<GitSequencerState> GetSequencerStateAsync(Guid projectId, CancellationToken ct = default);

    /// <summary>Moves HEAD to <paramref name="target"/>. Hard reset discards working-tree changes (confirm first).</summary>
    Task<GitOperationResult> ResetAsync(Guid projectId, GitResetMode mode, string target, CancellationToken ct = default);

    /// <summary>Applies the given commits onto the current branch; may pause on conflict.</summary>
    Task<GitOperationResult> CherryPickAsync(Guid projectId, IReadOnlyList<string> commits, CancellationToken ct = default);

    /// <summary>Reverts the given commits (<c>--no-edit</c>); may pause on conflict.</summary>
    Task<GitOperationResult> RevertAsync(Guid projectId, IReadOnlyList<string> commits, CancellationToken ct = default);

    /// <summary>Drives an in-progress sequencer operation with continue / abort / skip / quit.</summary>
    Task<GitOperationResult> RunSequencerAsync(Guid projectId, GitSequencerOperation operation, SequencerAction action, CancellationToken ct = default);

    /// <summary>Writes/updates the on-disk commit-graph to speed up history operations.</summary>
    Task<GitOperationResult> OptimizeCommitGraphAsync(Guid projectId, CancellationToken ct = default);

    // ---- Phase 6: tags ----

    Task<IReadOnlyList<GitTag>> GetTagsAsync(Guid projectId, CancellationToken ct = default);
    Task<GitOperationResult> CreateTagAsync(Guid projectId, GitTagRequest request, CancellationToken ct = default);
    Task<GitOperationResult> DeleteTagAsync(Guid projectId, string name, CancellationToken ct = default);
    Task<GitOperationResult> PushTagAsync(Guid projectId, string name, string? remote, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);
    Task<GitOperationResult> PushAllTagsAsync(Guid projectId, string? remote, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);
    Task<GitOperationResult> DeleteRemoteTagAsync(Guid projectId, string name, string? remote, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);

    // ---- Phase 6: worktrees ----

    Task<IReadOnlyList<GitWorktree>> GetWorktreesAsync(Guid projectId, CancellationToken ct = default);
    Task<GitOperationResult> AddWorktreeAsync(Guid projectId, string path, string? branch, bool newBranch, CancellationToken ct = default);
    Task<GitOperationResult> RemoveWorktreeAsync(Guid projectId, string path, bool force, CancellationToken ct = default);
    Task<GitOperationResult> PruneWorktreesAsync(Guid projectId, CancellationToken ct = default);

    // ---- Phase 6: submodules ----

    Task<IReadOnlyList<GitSubmodule>> GetSubmodulesAsync(Guid projectId, CancellationToken ct = default);
    Task<GitOperationResult> UpdateSubmodulesAsync(Guid projectId, bool init, bool recursive, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);
    Task<GitOperationResult> SyncSubmodulesAsync(Guid projectId, CancellationToken ct = default);

    // ---- Phase 6: Git LFS ----

    Task<GitLfsInfo> GetLfsInfoAsync(Guid projectId, CancellationToken ct = default);

    // ---- Phase 6: partial / line staging ----

    /// <summary>Stages (or with <paramref name="unstage"/>, unstages) only the selected changed lines of a file.</summary>
    Task<GitOperationResult> ApplySelectedLinesAsync(Guid projectId, GitDiffFile file, ISet<(int Hunk, int Line)> selected, bool unstage, CancellationToken ct = default);

    // ---- Phase 6: interactive rebase ----

    /// <summary>The most recent <paramref name="count"/> commits, oldest-first, to seed an interactive-rebase plan.</summary>
    Task<IReadOnlyList<GitCommit>> GetRebaseCommitsAsync(Guid projectId, int count, CancellationToken ct = default);

    /// <summary>Runs an interactive rebase from the plan, driving the sequence/message editors non-interactively.</summary>
    Task<GitOperationResult> StartInteractiveRebaseAsync(Guid projectId, GitRebasePlan plan, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default);

    // ---- Phase 6: conflict editor ----

    /// <summary>Reads the base/ours/theirs stages and the marked-up working copy of a conflicted file.</summary>
    Task<GitConflictFile> GetConflictAsync(Guid projectId, string path, CancellationToken ct = default);

    /// <summary>Writes the resolved content to the file and stages it, marking the conflict resolved.</summary>
    Task<GitOperationResult> ResolveConflictAsync(Guid projectId, string path, string content, CancellationToken ct = default);

    /// <summary>Takes one whole side of a conflict and stages the result.</summary>
    Task<GitOperationResult> TakeConflictSideAsync(Guid projectId, string path, GitConflictSide side, CancellationToken ct = default);
}
