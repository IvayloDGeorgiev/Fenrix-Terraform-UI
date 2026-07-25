using System.Text;
using Fenrix.IaCStudio.Application.Abstractions.Git;
using Fenrix.IaCStudio.Application.Abstractions.Projects;
using Fenrix.IaCStudio.Application.Abstractions.Terraform;
using Fenrix.IaCStudio.Application.Git;
using Fenrix.IaCStudio.Contracts.Git;
using Fenrix.IaCStudio.Contracts.Terraform;
using Fenrix.IaCStudio.Domain.Common;
using Fenrix.IaCStudio.Domain.Git;
using Microsoft.Extensions.Logging;

namespace Fenrix.IaCStudio.Infrastructure.Git;

/// <summary>
/// Drives the official Git CLI through the shared <c>ArgumentList</c> process runner. Mutating operations
/// (init, clone, stage, commit, fetch/pull/push, branch, merge, stash write) are recorded as redacted
/// history via <see cref="GitProcessCoordinator"/>; read-only queries (status, log, diff, branch/stash
/// list, rev-parse) run silently so frequent UI refreshes don't spam history. Every command is built by
/// <see cref="GitCommandCatalog"/>, so the preview shown in the UI is exactly what executes. See
/// docs/08-git-engine.md and docs/23-command-transparency.md.
/// </summary>
public sealed class GitService(
    IProjectService projects,
    IGitDiscovery discovery,
    IProcessRunner runner,
    GitProcessCoordinator coordinator,
    ILogger<GitService> logger) : IGitService
{
    private const string DefaultExecutable = "git";
    private static readonly IReadOnlyDictionary<string, string> NoEnv = new Dictionary<string, string>(0);

    private readonly IProjectService _projects = projects;
    private readonly IGitDiscovery _discovery = discovery;
    private readonly IProcessRunner _runner = runner;
    private readonly GitProcessCoordinator _coordinator = coordinator;
    private readonly ILogger<GitService> _logger = logger;

    // ---- repository ----

    public async Task<GitCommandContext?> ResolveContextAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return null;

        var install = await _discovery.ResolveAsync(projectId, ct);
        var exe = install?.ExecutablePath ?? DefaultExecutable;
        var isRepo = false;
        var workingDir = project.RootPath;

        if (install is not null)
        {
            var top = await RunSilentAsync(Request(GitCommandCatalog.RevParseTopLevel(), projectId, exe, project.RootPath), ct);
            if (top.Result.Succeeded && !string.IsNullOrWhiteSpace(top.StdOut))
            {
                isRepo = true;
                workingDir = NormalizePath(top.StdOut.Trim());
            }
        }

        return new GitCommandContext(projectId, exe, workingDir, isRepo, install?.Version);
    }

    public async Task<GitRepositoryInfo> DetectAsync(Guid projectId, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return GitRepositoryInfo.None;

        var status = await ReadStatusAsync(ctx, ct);
        return new GitRepositoryInfo(true, ctx.WorkingDirectory, status.Branch, status.Oid, status.IsDetached);
    }

    public async Task<GitOperationResult> InitAsync(Guid projectId, CancellationToken ct = default)
    {
        var project = await _projects.GetAsync(projectId, ct);
        if (project is null)
            return GitOperationResult.Fail("Project not found.");
        return await InitAtAsync(project.RootPath, ct);
    }

    public async Task<GitOperationResult> InitAtAsync(string directory, CancellationToken ct = default)
    {
        var install = await _discovery.ResolveAsync(null, ct);
        if (install is null)
            return GitOperationResult.Fail("No Git binary found. Set the executable in Settings or install Git on your PATH.");

        Directory.CreateDirectory(directory);
        var request = Request(GitCommandCatalog.Init(), Guid.Empty, install.ExecutablePath, directory);
        var run = await _coordinator.RunAsync(request, output: null, captureLog: true, ct);
        return ToResult(run);
    }

    public async Task<GitOperationResult> CloneAsync(
        GitCloneRequest request, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default)
    {
        var install = await _discovery.ResolveAsync(null, ct);
        if (install is null)
            return GitOperationResult.Fail("No Git binary found. Set the executable in Settings or install Git on your PATH.");
        if (string.IsNullOrWhiteSpace(request.Url))
            return GitOperationResult.Fail("A repository URL is required.");
        if (Directory.Exists(request.DestinationPath) && Directory.EnumerateFileSystemEntries(request.DestinationPath).Any())
            return GitOperationResult.Fail($"The destination '{request.DestinationPath}' already exists and is not empty.");

        Directory.CreateDirectory(request.DestinationParent);
        var def = GitCommandCatalog.Clone(request.Url, request.FolderName);
        var req = Request(def, Guid.Empty, install.ExecutablePath, request.DestinationParent);
        var run = await _coordinator.RunAsync(req, output, captureLog: true, ct);
        return ToResult(run);
    }

    public async Task<GitProvenance> ReadProvenanceAsync(string workingDirectory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return GitProvenance.None;

        var install = await _discovery.ResolveAsync(null, ct);
        if (install is null)
            return GitProvenance.None;

        var exe = install.ExecutablePath;
        var top = await RunSilentAsync(Request(GitCommandCatalog.RevParseTopLevel(), Guid.Empty, exe, workingDirectory), ct);
        if (!top.Result.Succeeded)
            return GitProvenance.None;

        var head = (await RunSilentAsync(Request(GitCommandCatalog.RevParseHead(), Guid.Empty, exe, workingDirectory), ct)).StdOut.Trim();
        var branch = (await RunSilentAsync(Request(GitCommandCatalog.CurrentBranch(), Guid.Empty, exe, workingDirectory), ct)).StdOut.Trim();
        var statusOut = (await RunSilentAsync(Request(GitCommandCatalog.Status(), Guid.Empty, exe, workingDirectory), ct)).StdOut;
        var dirty = GitStatusParser.Parse(statusOut).HasChanges;

        return new GitProvenance(
            true,
            string.IsNullOrEmpty(head) ? null : head,
            string.IsNullOrEmpty(branch) || branch == "HEAD" ? null : branch,
            dirty);
    }

    // ---- working tree ----

    public async Task<GitStatus> GetStatusAsync(Guid projectId, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return GitStatus.NotARepository;
        return await ReadStatusAsync(ctx, ct);
    }

    public Task<GitOperationResult> StageAsync(Guid projectId, IReadOnlyList<string> paths, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.Stage(paths), ct);

    public Task<GitOperationResult> StageAllAsync(Guid projectId, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.StageAll(), ct);

    public Task<GitOperationResult> UnstageAsync(Guid projectId, IReadOnlyList<string> paths, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.Unstage(paths), ct);

    public Task<GitOperationResult> DiscardAsync(Guid projectId, IReadOnlyList<string> paths, bool untracked, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.Discard(paths, untracked), ct);

    public async Task<GitOperationResult> CommitAsync(Guid projectId, GitCommitRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            return GitOperationResult.Fail("A commit message is required.");

        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null) return GitOperationResult.Fail("Project not found.");
        if (!ctx.IsRepository) return GitOperationResult.Fail("This project is not a Git repository.");

        if (request.StageAll)
        {
            var staged = await RunTrackedAsync(ctx, GitCommandCatalog.StageAll(), null, ct);
            if (!staged.Succeeded)
                return ToResult(staged);
        }

        var run = await RunTrackedAsync(ctx, GitCommandCatalog.Commit(request), null, ct);
        return ToResult(run);
    }

    // ---- remotes ----

    public Task<GitOperationResult> FetchAsync(Guid projectId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.Fetch(null, prune: true), ct, output);

    public Task<GitOperationResult> PullAsync(Guid projectId, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.Pull(ffOnly: false), ct, output);

    public Task<GitOperationResult> PushAsync(Guid projectId, bool forceWithLease, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.Push(null, null, setUpstream: false, forceWithLease), ct, output);

    // ---- branches ----

    public async Task<IReadOnlyList<GitBranch>> GetBranchesAsync(Guid projectId, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return [];
        var raw = (await RunSilentAsync(Request(GitCommandCatalog.BranchList(), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        return GitBranchParser.Parse(raw);
    }

    public Task<GitOperationResult> CreateBranchAsync(Guid projectId, string name, bool checkout, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.BranchCreate(name, startPoint: null, checkout), ct);

    public Task<GitOperationResult> CheckoutAsync(Guid projectId, string name, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.Checkout(name), ct);

    public Task<GitOperationResult> RenameBranchAsync(Guid projectId, string oldName, string newName, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.BranchRename(oldName, newName), ct);

    public Task<GitOperationResult> DeleteBranchAsync(Guid projectId, string name, bool force, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.BranchDelete(name, force), ct);

    public async Task<GitMergeResult> MergeAsync(Guid projectId, string branch, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return new GitMergeResult(false, false, [], Guid.Empty, "This project is not a Git repository.");

        var run = await RunTrackedAsync(ctx, GitCommandCatalog.Merge(branch, noFastForward: false), null, ct);
        if (run.Succeeded)
            return new GitMergeResult(true, false, [], run.RunId, run.FullOutput);

        // Non-zero exit: inspect the working tree for conflict markers.
        var status = await ReadStatusAsync(ctx, ct);
        var conflicts = status.Conflicted.Select(e => e.Path).ToList();
        var message = string.IsNullOrWhiteSpace(run.StandardError) ? run.FullOutput : run.StandardError;
        return new GitMergeResult(false, conflicts.Count > 0, conflicts, run.RunId, message);
    }

    public Task<GitOperationResult> AbortMergeAsync(Guid projectId, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.MergeAbort(), ct);

    // ---- history & inspection ----

    public async Task<IReadOnlyList<GitCommit>> GetHistoryAsync(Guid projectId, int limit = 100, string? path = null, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return [];
        var raw = (await RunSilentAsync(Request(GitCommandCatalog.Log(limit, path), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        return GitLogParser.Parse(raw);
    }

    public async Task<IReadOnlyList<GitDiffFile>> GetDiffAsync(Guid projectId, GitDiffSpec spec, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return [];

        if (spec.Source == GitDiffSource.Untracked)
        {
            if (string.IsNullOrEmpty(spec.Path))
                return [];
            var full = Path.Combine(ctx.WorkingDirectory, spec.Path);
            if (!File.Exists(full))
                return [];
            try
            {
                var content = await File.ReadAllTextAsync(full, ct);
                return [GitDiffParser.FromUntracked(spec.Path, content)];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        var raw = (await RunSilentAsync(Request(GitCommandCatalog.Diff(spec), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        return GitDiffParser.Parse(raw);
    }

    // ---- stash ----

    public async Task<IReadOnlyList<GitStash>> GetStashesAsync(Guid projectId, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return [];
        var raw = (await RunSilentAsync(Request(GitCommandCatalog.StashList(), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        return GitStashParser.Parse(raw);
    }

    public Task<GitOperationResult> StashPushAsync(Guid projectId, string? message, bool includeUntracked, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.StashPush(message, includeUntracked), ct);

    public Task<GitOperationResult> StashApplyAsync(Guid projectId, int index, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.StashApply(index), ct);

    public Task<GitOperationResult> StashPopAsync(Guid projectId, int index, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.StashPop(index), ct);

    public Task<GitOperationResult> StashDropAsync(Guid projectId, int index, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.StashDrop(index), ct);

    // ---- Phase 6: inspection & local history rewriting ----

    public async Task<IReadOnlyList<GitReflogEntry>> GetReflogAsync(Guid projectId, int limit = 100, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return [];
        var raw = (await RunSilentAsync(Request(GitCommandCatalog.Reflog(limit), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        return GitReflogParser.Parse(raw);
    }

    public async Task<GitBlame> GetBlameAsync(Guid projectId, string path, string? revision = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
            return GitBlame.Empty(path);
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return GitBlame.Empty(path);
        var raw = (await RunSilentAsync(Request(GitCommandCatalog.Blame(path, revision), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        return GitBlameParser.Parse(path, raw);
    }

    public async Task<GitSequencerState> GetSequencerStateAsync(Guid projectId, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return GitSequencerState.Idle;

        var gitDir = (await RunSilentAsync(Request(GitCommandCatalog.RevParseGitDir(), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut.Trim();
        if (string.IsNullOrEmpty(gitDir) || !Directory.Exists(gitDir))
            return GitSequencerState.Idle;

        var operation =
            Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply"))
                ? GitSequencerOperation.Rebase
            : File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")) ? GitSequencerOperation.CherryPick
            : File.Exists(Path.Combine(gitDir, "REVERT_HEAD")) ? GitSequencerOperation.Revert
            : File.Exists(Path.Combine(gitDir, "MERGE_HEAD")) ? GitSequencerOperation.Merge
            : GitSequencerOperation.None;

        if (operation == GitSequencerOperation.None)
            return GitSequencerState.Idle;

        var status = await ReadStatusAsync(ctx, ct);
        var conflicts = status.Conflicted.Select(e => e.Path).ToList();
        var onto = ReadFirstLine(Path.Combine(gitDir, "rebase-merge", "onto"))
                   ?? ReadFirstLine(Path.Combine(gitDir, "rebase-merge", "head-name"));
        return new GitSequencerState(operation, conflicts.Count > 0, conflicts, status.Oid, onto);
    }

    public Task<GitOperationResult> ResetAsync(Guid projectId, GitResetMode mode, string target, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.Reset(mode, string.IsNullOrWhiteSpace(target) ? "HEAD" : target), ct);

    public Task<GitOperationResult> CherryPickAsync(Guid projectId, IReadOnlyList<string> commits, CancellationToken ct = default) =>
        commits is { Count: > 0 }
            ? MutateAsync(projectId, _ => GitCommandCatalog.CherryPick(commits), ct)
            : Task.FromResult(GitOperationResult.Fail("Select at least one commit to cherry-pick."));

    public Task<GitOperationResult> RevertAsync(Guid projectId, IReadOnlyList<string> commits, CancellationToken ct = default) =>
        commits is { Count: > 0 }
            ? MutateAsync(projectId, _ => GitCommandCatalog.Revert(commits), ct)
            : Task.FromResult(GitOperationResult.Fail("Select at least one commit to revert."));

    public Task<GitOperationResult> RunSequencerAsync(Guid projectId, GitSequencerOperation operation, SequencerAction action, CancellationToken ct = default)
    {
        var verb = operation switch
        {
            GitSequencerOperation.Merge => "merge",
            GitSequencerOperation.CherryPick => "cherry-pick",
            GitSequencerOperation.Revert => "revert",
            GitSequencerOperation.Rebase => "rebase",
            _ => null
        };
        if (verb is null)
            return Task.FromResult(GitOperationResult.Fail("No operation is in progress."));
        // git merge has no --skip/--quit; guard the unsupported combinations.
        if (operation == GitSequencerOperation.Merge && action is SequencerAction.Skip or SequencerAction.Quit)
            return Task.FromResult(GitOperationResult.Fail("Merge supports only continue or abort."));
        return MutateAsync(projectId, _ => GitCommandCatalog.Sequencer(verb, action), ct);
    }

    public Task<GitOperationResult> OptimizeCommitGraphAsync(Guid projectId, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.CommitGraphWrite(), ct);

    private static string? ReadFirstLine(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var line = File.ReadLines(path).FirstOrDefault()?.Trim();
            return string.IsNullOrEmpty(line) ? null : line;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ---- Phase 6: tags ----

    public async Task<IReadOnlyList<GitTag>> GetTagsAsync(Guid projectId, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return [];
        var raw = (await RunSilentAsync(Request(GitCommandCatalog.TagList(), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        return GitTagParser.Parse(raw);
    }

    public Task<GitOperationResult> CreateTagAsync(Guid projectId, GitTagRequest request, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(request.Name)
            ? Task.FromResult(GitOperationResult.Fail("A tag name is required."))
            : MutateAsync(projectId, _ => GitCommandCatalog.TagCreate(request), ct);

    public Task<GitOperationResult> DeleteTagAsync(Guid projectId, string name, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.TagDelete(name), ct);

    public Task<GitOperationResult> PushTagAsync(Guid projectId, string name, string? remote, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.TagPush(Remote(remote), name), ct, output);

    public Task<GitOperationResult> PushAllTagsAsync(Guid projectId, string? remote, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.TagPushAll(Remote(remote)), ct, output);

    public Task<GitOperationResult> DeleteRemoteTagAsync(Guid projectId, string name, string? remote, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.TagDeleteRemote(Remote(remote), name), ct, output);

    // ---- Phase 6: worktrees ----

    public async Task<IReadOnlyList<GitWorktree>> GetWorktreesAsync(Guid projectId, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return [];
        var raw = (await RunSilentAsync(Request(GitCommandCatalog.WorktreeList(), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        return GitWorktreeParser.Parse(raw);
    }

    public Task<GitOperationResult> AddWorktreeAsync(Guid projectId, string path, string? branch, bool newBranch, CancellationToken ct = default) =>
        string.IsNullOrWhiteSpace(path)
            ? Task.FromResult(GitOperationResult.Fail("A worktree path is required."))
            : MutateAsync(projectId, _ => GitCommandCatalog.WorktreeAdd(path, branch, newBranch), ct);

    public Task<GitOperationResult> RemoveWorktreeAsync(Guid projectId, string path, bool force, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.WorktreeRemove(path, force), ct);

    public Task<GitOperationResult> PruneWorktreesAsync(Guid projectId, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.WorktreePrune(), ct);

    // ---- Phase 6: submodules ----

    public async Task<IReadOnlyList<GitSubmodule>> GetSubmodulesAsync(Guid projectId, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return [];
        var raw = (await RunSilentAsync(Request(GitCommandCatalog.SubmoduleStatus(), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        return GitSubmoduleParser.Parse(raw);
    }

    public Task<GitOperationResult> UpdateSubmodulesAsync(Guid projectId, bool init, bool recursive, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.SubmoduleUpdate(init, recursive), ct, output);

    public Task<GitOperationResult> SyncSubmodulesAsync(Guid projectId, CancellationToken ct = default) =>
        MutateAsync(projectId, _ => GitCommandCatalog.SubmoduleSync(recursive: true), ct);

    // ---- Phase 6: Git LFS (indicators only) ----

    public async Task<GitLfsInfo> GetLfsInfoAsync(Guid projectId, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return GitLfsInfo.NotInstalled;

        var version = await RunSilentAsync(Request(GitCommandCatalog.LfsVersion(), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct);
        var installed = version.Result.Succeeded && version.StdOut.Contains("git-lfs", StringComparison.OrdinalIgnoreCase);
        if (!installed)
            return GitLfsInfo.NotInstalled;

        var track = (await RunSilentAsync(Request(GitCommandCatalog.LfsTrack(), projectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        var patterns = GitLfsParser.ParseTrackedPatterns(track);
        return new GitLfsInfo(true, patterns.Count > 0, patterns);
    }

    private static string Remote(string? remote) => string.IsNullOrWhiteSpace(remote) ? "origin" : remote;

    // ---- Phase 6: partial / line staging ----

    public async Task<GitOperationResult> ApplySelectedLinesAsync(
        Guid projectId, GitDiffFile file, ISet<(int Hunk, int Line)> selected, bool unstage, CancellationToken ct = default)
    {
        var patch = GitPatchBuilder.Build(file, selected, reverse: unstage);
        if (patch is null)
            return GitOperationResult.Fail("No lines selected.");

        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null) return GitOperationResult.Fail("Project not found.");
        if (!ctx.IsRepository) return GitOperationResult.Fail("This project is not a Git repository.");

        var patchFile = Path.Combine(Path.GetTempPath(), $"fenrix-{Guid.NewGuid():N}.patch");
        try
        {
            // Git patches must use LF line endings regardless of platform.
            await File.WriteAllTextAsync(patchFile, patch.Replace("\r\n", "\n"), new UTF8Encoding(false), ct);
            var run = await RunTrackedAsync(ctx, GitCommandCatalog.ApplyPatch(patchFile, unstage), null, ct);
            return ToResult(run);
        }
        finally
        {
            TryDelete(patchFile);
        }
    }

    // ---- Phase 6: interactive rebase ----

    public async Task<IReadOnlyList<GitCommit>> GetRebaseCommitsAsync(Guid projectId, int count, CancellationToken ct = default)
    {
        var commits = await GetHistoryAsync(projectId, Math.Max(1, count), null, ct);
        // Oldest-first so the plan order matches the rebase todo order.
        return commits.Reverse().ToList();
    }

    public async Task<GitOperationResult> StartInteractiveRebaseAsync(
        Guid projectId, GitRebasePlan plan, IProgress<ProcessOutputEvent>? output, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plan.Base))
            return GitOperationResult.Fail("A rebase base is required.");

        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null) return GitOperationResult.Fail("Project not found.");
        if (!ctx.IsRepository) return GitOperationResult.Fail("This project is not a Git repository.");

        var workDir = Path.Combine(Path.GetTempPath(), $"fenrix-rebase-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workDir);
        try
        {
            var env = new Dictionary<string, string>
            {
                // A no-op editor keeps default messages (e.g. squash's combined message) without blocking.
                ["GIT_EDITOR"] = "true"
            };

            if (!plan.Autosquash)
            {
                var todoPath = Path.Combine(workDir, "todo");
                var todo = BuildRebaseTodo(plan, workDir);
                await File.WriteAllTextAsync(todoPath, todo, new UTF8Encoding(false), ct);
                // git runs the sequence editor via its shell, appending the real todo path as an argument;
                // `cp <ourTodo>` overwrites it with the plan we built.
                env["GIT_SEQUENCE_EDITOR"] = $"cp \"{ToShellPath(todoPath)}\"";
            }
            else
            {
                // Accept Git's autosquash-ordered todo unchanged.
                env["GIT_SEQUENCE_EDITOR"] = "true";
            }

            var def = GitCommandCatalog.RebaseInteractive(plan.Base, plan.Autosquash);
            var request = GitCommandPreviewBuilder.BuildRequest(def, ctx.ProjectId, ctx.ExecutablePath, ctx.WorkingDirectory, env);
            var run = await _coordinator.RunAsync(request, output, captureLog: true, ct);
            return ToResult(run);
        }
        finally
        {
            TryDeleteDirectory(workDir);
        }
    }

    private static string BuildRebaseTodo(GitRebasePlan plan, string workDir)
    {
        var sb = new StringBuilder();
        var rewordIndex = 0;
        foreach (var step in plan.Steps)
        {
            switch (step.Action)
            {
                case RebaseAction.Pick: sb.Append("pick ").Append(step.Sha).Append('\n'); break;
                case RebaseAction.Edit: sb.Append("edit ").Append(step.Sha).Append('\n'); break;
                case RebaseAction.Squash: sb.Append("squash ").Append(step.Sha).Append('\n'); break;
                case RebaseAction.Fixup: sb.Append("fixup ").Append(step.Sha).Append('\n'); break;
                case RebaseAction.Drop: sb.Append("drop ").Append(step.Sha).Append('\n'); break;
                case RebaseAction.Reword:
                    // pick, then amend the message from a file so no interactive editor is needed.
                    var msgPath = Path.Combine(workDir, $"msg{rewordIndex++}.txt");
                    File.WriteAllText(msgPath, (step.NewMessage ?? step.Subject).Replace("\r\n", "\n"), new UTF8Encoding(false));
                    sb.Append("pick ").Append(step.Sha).Append('\n');
                    sb.Append("exec git commit --amend -F \"").Append(ToShellPath(msgPath)).Append("\"\n");
                    break;
            }
        }
        return sb.ToString();
    }

    // ---- Phase 6: conflict editor ----

    public async Task<GitConflictFile> GetConflictAsync(Guid projectId, string path, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null || !ctx.IsRepository)
            return new GitConflictFile(path, null, null, null, string.Empty);

        var baseText = await ReadStageAsync(ctx, 1, path, ct);
        var ours = await ReadStageAsync(ctx, 2, path, ct);
        var theirs = await ReadStageAsync(ctx, 3, path, ct);

        var merged = string.Empty;
        try
        {
            var full = Path.Combine(ctx.WorkingDirectory, path);
            if (File.Exists(full))
                merged = await File.ReadAllTextAsync(full, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read conflicted file {Path}.", path);
        }

        return new GitConflictFile(path, baseText, ours, theirs, merged);
    }

    public async Task<GitOperationResult> ResolveConflictAsync(Guid projectId, string path, string content, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null) return GitOperationResult.Fail("Project not found.");
        if (!ctx.IsRepository) return GitOperationResult.Fail("This project is not a Git repository.");

        try
        {
            var full = Path.Combine(ctx.WorkingDirectory, path);
            await File.WriteAllTextAsync(full, content, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return GitOperationResult.Fail($"Could not write '{path}': {ex.Message}");
        }

        var run = await RunTrackedAsync(ctx, GitCommandCatalog.Stage([path]), null, ct);
        return ToResult(run);
    }

    public async Task<GitOperationResult> TakeConflictSideAsync(Guid projectId, string path, GitConflictSide side, CancellationToken ct = default)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null) return GitOperationResult.Fail("Project not found.");
        if (!ctx.IsRepository) return GitOperationResult.Fail("This project is not a Git repository.");

        var checkout = await RunTrackedAsync(ctx, GitCommandCatalog.CheckoutConflictSide(side, path), null, ct);
        if (!checkout.Succeeded)
            return ToResult(checkout);
        var staged = await RunTrackedAsync(ctx, GitCommandCatalog.Stage([path]), null, ct);
        return ToResult(staged);
    }

    private async Task<string?> ReadStageAsync(GitCommandContext ctx, int stage, string path, CancellationToken ct)
    {
        var r = await RunSilentAsync(Request(GitCommandCatalog.ShowStage(stage, path), ctx.ProjectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct);
        return r.Result.Succeeded ? r.StdOut : null;
    }

    private static string ToShellPath(string path) => path.Replace('\\', '/');

    private void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { _logger.LogDebug(ex, "Could not delete temp file {Path}.", path); }
    }

    private void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception ex) { _logger.LogDebug(ex, "Could not delete temp dir {Path}.", path); }
    }

    // ---- internals ----

    private async Task<GitStatus> ReadStatusAsync(GitCommandContext ctx, CancellationToken ct)
    {
        var raw = (await RunSilentAsync(Request(GitCommandCatalog.Status(), ctx.ProjectId, ctx.ExecutablePath, ctx.WorkingDirectory), ct)).StdOut;
        return GitStatusParser.Parse(raw);
    }

    private async Task<GitOperationResult> MutateAsync(
        Guid projectId,
        Func<GitCommandContext, GitCommandCatalog.GitCommandDefinition> build,
        CancellationToken ct,
        IProgress<ProcessOutputEvent>? output = null)
    {
        var ctx = await ResolveContextAsync(projectId, ct);
        if (ctx is null) return GitOperationResult.Fail("Project not found.");
        if (!ctx.IsRepository) return GitOperationResult.Fail("This project is not a Git repository.");

        var run = await RunTrackedAsync(ctx, build(ctx), output, ct);
        return ToResult(run);
    }

    private Task<GitProcessCoordinator.CoordinatedRun> RunTrackedAsync(
        GitCommandContext ctx, GitCommandCatalog.GitCommandDefinition def, IProgress<ProcessOutputEvent>? output, CancellationToken ct)
    {
        var request = Request(def, ctx.ProjectId, ctx.ExecutablePath, ctx.WorkingDirectory);
        return _coordinator.RunAsync(request, output, captureLog: true, ct);
    }

    private async Task<(ProcessResult Result, string StdOut, string StdErr)> RunSilentAsync(
        GitCommandRequest request, CancellationToken ct)
    {
        var env = new Dictionary<string, string>(request.EnvironmentVariables) { ["GIT_TERMINAL_PROMPT"] = "0" };
        var start = new ProcessStartRequest(
            request.ExecutablePath, request.WorkingDirectory, request.Arguments, env, $"git {request.Command}");

        var collector = new SyncCollector();
        try
        {
            var result = await _runner.RunAsync(start, collector, ct);
            return (result, collector.Out.ToString(), collector.Err.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "git {Command} (read) failed.", request.Command);
            return (new ProcessResult(-1, false, DateTimeOffset.Now, DateTimeOffset.Now), string.Empty, ex.Message);
        }
    }

    private static GitCommandRequest Request(
        GitCommandCatalog.GitCommandDefinition def, Guid projectId, string exe, string workingDir) =>
        GitCommandPreviewBuilder.BuildRequest(def, projectId, exe, workingDir, NoEnv);

    private static GitOperationResult ToResult(GitProcessCoordinator.CoordinatedRun run)
    {
        var error = run.Succeeded
            ? null
            : string.IsNullOrWhiteSpace(run.StandardError) ? run.FullOutput.Trim() : run.StandardError.Trim();
        return new GitOperationResult(run.Succeeded, run.Process.ExitCode, run.RunId, run.FullOutput, error);
    }

    private static string NormalizePath(string path) =>
        OperatingSystem.IsWindows() ? path.Replace('/', '\\') : path;

    private sealed class SyncCollector : IProgress<ProcessOutputEvent>
    {
        public StringBuilder Out { get; } = new();
        public StringBuilder Err { get; } = new();

        public void Report(ProcessOutputEvent value)
        {
            if (value.Stream == OutputStream.Stdout) Out.AppendLine(value.Text);
            else Err.AppendLine(value.Text);
        }
    }
}
