using Fenrix.IaCStudio.Contracts.Git;
using Fenrix.IaCStudio.Contracts.Terraform;

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
}
