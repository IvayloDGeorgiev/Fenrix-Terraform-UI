namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>What the user wants a commit to do. See docs/08-git-engine.md.</summary>
public sealed record GitCommitRequest(
    string Message,
    bool StageAll = false,
    bool Amend = false,
    bool SignOff = false);

/// <summary>
/// A request to clone a remote into a destination folder. When <see cref="SparsePaths"/> is non-empty the
/// clone is a partial + sparse checkout (<c>--filter=blob:none --sparse</c>) that materialises only those
/// directories — used to check out a single environment's path. See docs/08-git-engine.md.
/// </summary>
public sealed record GitCloneRequest(
    string Url, string DestinationParent, string FolderName, IReadOnlyList<string>? SparsePaths = null)
{
    /// <summary>The full destination path the clone will be created at.</summary>
    public string DestinationPath => System.IO.Path.Combine(DestinationParent, FolderName);

    public bool IsSparse => SparsePaths is { Count: > 0 };
}

/// <summary>
/// The outcome of a Git command: exit status, the recorded history run id, and captured output for the UI
/// to surface (already produced by an argument list whose preview was shown first). See
/// docs/08-git-engine.md, docs/23-command-transparency.md.
/// </summary>
public sealed record GitOperationResult(
    bool Succeeded,
    int ExitCode,
    Guid RunId,
    string Output,
    string? Error)
{
    public static GitOperationResult Fail(string error) =>
        new(false, -1, Guid.Empty, string.Empty, error);
}

/// <summary>
/// The result of a merge attempt. When <see cref="HasConflicts"/> is true the merge stopped with the
/// listed paths left conflicted in the working tree; Phase 5 detects and surfaces these (the in-app
/// conflict editor is Phase 6) and offers <c>merge --abort</c>. See docs/08-git-engine.md.
/// </summary>
public sealed record GitMergeResult(
    bool Succeeded,
    bool HasConflicts,
    IReadOnlyList<string> ConflictedPaths,
    Guid RunId,
    string Output)
{
    public bool FastForwardOrClean => Succeeded && !HasConflicts;
}

/// <summary>
/// Git provenance captured for a saved plan (Phase 4 wiring): the commit the plan was built at, the branch,
/// and whether the working tree had uncommitted changes. All null/false when the project is not a repo.
/// See docs/06-plan-apply-safety.md and docs/08-git-engine.md.
/// </summary>
public sealed record GitProvenance(bool IsRepository, string? CommitSha, string? Branch, bool IsDirty)
{
    public static GitProvenance None { get; } = new(false, null, null, false);
}

/// <summary>
/// The resolved binary + repository working directory for a project, so the UI can build live, redacted
/// command previews from the same catalog the service executes (e.g. as a commit message is typed). See
/// docs/08-git-engine.md and docs/23-command-transparency.md.
/// </summary>
public sealed record GitCommandContext(
    Guid ProjectId,
    string ExecutablePath,
    string WorkingDirectory,
    bool IsRepository,
    string? GitVersion);

/// <summary>
/// Lightweight repository info from detection: whether the working directory is inside a repo, the repo
/// root, the current branch/HEAD, and whether HEAD is detached. See docs/08-git-engine.md.
/// </summary>
public sealed record GitRepositoryInfo(
    bool IsRepository,
    string? RepositoryRoot,
    string? CurrentBranch,
    string? HeadSha,
    bool IsDetached)
{
    public static GitRepositoryInfo None { get; } = new(false, null, null, null, false);
}
