using Fenrix.IaCStudio.Contracts.Git;
using Fenrix.IaCStudio.Domain.Git;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Builds the exact, ordered argument list for every Git operation Fenrix drives. This is the <em>single</em>
/// source of truth for what <c>git</c> is invoked with: both the live command preview and the executed
/// process are generated from the list returned here, so they can never diverge. The first element is
/// always the git subcommand. Arguments are passed via <c>ArgumentList</c> — never a shell string — so
/// paths, URLs and messages with spaces or special characters are safe. See docs/08-git-engine.md and
/// docs/23-command-transparency.md.
/// </summary>
public static class GitCommandCatalog
{
    /// <summary>The resolved subcommand, its full argument list (subcommand first), risk, and remote flag.</summary>
    public readonly record struct GitCommandDefinition(
        GitCommandKind Kind,
        string Command,
        IReadOnlyList<string> Arguments,
        GitOperationRisk Risk,
        bool TargetsRemote = false);

    /// <summary>NUL-delimited fields + 0x1e record terminator so commit subjects/bodies stay intact.</summary>
    public const string LogFormat = "%H%x00%h%x00%an%x00%ae%x00%aI%x00%P%x00%s%x00%b%x1e";

    /// <summary>NUL-delimited branch fields: HEAD marker, refname, short name, upstream, track, tip, subject.</summary>
    public const string BranchFormat =
        "%(HEAD)%00%(refname)%00%(refname:short)%00%(upstream:short)%00%(upstream:track)%00%(objectname)%00%(subject)";

    /// <summary>Stash selector + reflog subject, NUL-delimited.</summary>
    public const string StashFormat = "%gd%x00%gs";

    /// <summary>Reflog fields: full sha, short sha, selector (HEAD@{n}), subject, author, ISO date; 0x1e record end.</summary>
    public const string ReflogFormat = "%H%x00%h%x00%gd%x00%gs%x00%an%x00%aI%x1e";

    /// <summary>Tag fields: refname:short, target object, tagger/creator date, and (annotated) subject; NUL-delimited.</summary>
    public const string TagFormat = "%(refname:short)%00%(objecttype)%00%(objectname)%00%(*objectname)%00%(creatordate:iso-strict)%00%(contents:subject)";

    // ---- repository ----

    public static GitCommandDefinition Version() =>
        new(GitCommandKind.Version, "version", ["--version"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition Init() =>
        new(GitCommandKind.Init, "init", ["init"], GitOperationRisk.Safe);

    public static GitCommandDefinition Clone(string url, string folderName, bool sparse = false)
    {
        var args = new List<string> { "clone", "--progress" };
        if (sparse)
        {
            // Partial + sparse: fetch no blobs up front and check out only top-level files until
            // sparse-checkout narrows to the requested directories (cone mode, git 2.25+).
            args.Add("--filter=blob:none");
            args.Add("--sparse");
        }
        args.Add(url);
        args.Add(folderName);
        return new(GitCommandKind.Clone, "clone", args, GitOperationRisk.StateChanging, TargetsRemote: true);
    }

    /// <summary>Narrows a sparse checkout to the given directories (cone mode). Run inside the cloned repo.</summary>
    public static GitCommandDefinition SparseCheckoutSet(IReadOnlyList<string> paths) =>
        new(GitCommandKind.Checkout, "sparse-checkout", Concat(["sparse-checkout", "set"], paths), GitOperationRisk.Safe);

    public static GitCommandDefinition RevParseTopLevel() =>
        new(GitCommandKind.RevParseTopLevel, "rev-parse", ["rev-parse", "--show-toplevel"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition RevParseHead() =>
        new(GitCommandKind.RevParseTopLevel, "rev-parse", ["rev-parse", "HEAD"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition CurrentBranch() =>
        new(GitCommandKind.RevParseTopLevel, "rev-parse", ["rev-parse", "--abbrev-ref", "HEAD"], GitOperationRisk.ReadOnly);

    /// <summary>Reads a remote's fetch URL (default <c>origin</c>) so the host repo id can be derived.</summary>
    public static GitCommandDefinition RemoteGetUrl(string? remote) =>
        new(GitCommandKind.RevParseTopLevel, "remote",
            ["remote", "get-url", string.IsNullOrWhiteSpace(remote) ? "origin" : remote], GitOperationRisk.ReadOnly);

    // ---- working tree ----

    public static GitCommandDefinition Status() =>
        new(GitCommandKind.Status, "status", ["status", "--porcelain=v2", "-z", "--branch"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition StageAll() =>
        new(GitCommandKind.StageAll, "add", ["add", "-A"], GitOperationRisk.Safe);

    public static GitCommandDefinition Stage(IReadOnlyList<string> paths) =>
        new(GitCommandKind.Stage, "add", Concat(["add", "--"], paths), GitOperationRisk.Safe);

    public static GitCommandDefinition UnstageAll() =>
        new(GitCommandKind.UnstageAll, "reset", ["reset", "-q", "HEAD"], GitOperationRisk.Safe);

    public static GitCommandDefinition Unstage(IReadOnlyList<string> paths) =>
        new(GitCommandKind.Unstage, "reset", Concat(["reset", "-q", "HEAD", "--"], paths), GitOperationRisk.Safe);

    /// <summary>
    /// Discards changes: for tracked paths, <c>checkout -- …</c> restores them from HEAD; for untracked
    /// paths, <c>clean -f -d -- …</c> deletes them. Both destroy uncommitted work → destructive.
    /// </summary>
    public static GitCommandDefinition Discard(IReadOnlyList<string> paths, bool untracked) =>
        untracked
            ? new(GitCommandKind.Discard, "clean", Concat(["clean", "-f", "-d", "--"], paths), GitOperationRisk.Destructive)
            : new(GitCommandKind.Discard, "checkout", Concat(["checkout", "--"], paths), GitOperationRisk.Destructive);

    public static GitCommandDefinition Commit(GitCommitRequest req)
    {
        var args = new List<string> { "commit" };
        if (req.Amend) args.Add("--amend");
        if (req.SignOff) args.Add("--signoff");
        args.Add("-m");
        args.Add(req.Message);
        return new(GitCommandKind.Commit, "commit", args, GitOperationRisk.Safe);
    }

    // ---- remotes (local-only posture: run non-interactively) ----

    public static GitCommandDefinition Fetch(string? remote, bool prune)
    {
        var args = new List<string> { "fetch" };
        if (string.IsNullOrWhiteSpace(remote)) args.Add("--all");
        else args.Add(remote);
        if (prune) args.Add("--prune");
        return new(GitCommandKind.Fetch, "fetch", args, GitOperationRisk.StateChanging, TargetsRemote: true);
    }

    public static GitCommandDefinition Pull(bool ffOnly)
    {
        var args = new List<string> { "pull" };
        if (ffOnly) args.Add("--ff-only");
        return new(GitCommandKind.Pull, "pull", args, GitOperationRisk.StateChanging, TargetsRemote: true);
    }

    public static GitCommandDefinition Push(string? remote, string? branch, bool setUpstream, bool forceWithLease)
    {
        var args = new List<string> { "push" };
        if (forceWithLease) args.Add("--force-with-lease");
        if (setUpstream) args.Add("-u");
        if (!string.IsNullOrWhiteSpace(remote)) args.Add(remote);
        if (!string.IsNullOrWhiteSpace(branch)) args.Add(branch);
        var risk = forceWithLease ? GitOperationRisk.Destructive : GitOperationRisk.StateChanging;
        return new(GitCommandKind.Push, "push", args, risk, TargetsRemote: true);
    }

    // ---- branches ----

    public static GitCommandDefinition BranchList() =>
        new(GitCommandKind.BranchList, "branch", ["branch", "--all", $"--format={BranchFormat}"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition BranchCreate(string name, string? startPoint, bool checkout)
    {
        var args = checkout ? new List<string> { "checkout", "-b", name } : new List<string> { "branch", name };
        if (!string.IsNullOrWhiteSpace(startPoint)) args.Add(startPoint);
        var command = checkout ? "checkout" : "branch";
        return new(GitCommandKind.BranchCreate, command, args, GitOperationRisk.Safe);
    }

    public static GitCommandDefinition Checkout(string name) =>
        new(GitCommandKind.Checkout, "checkout", ["checkout", name], GitOperationRisk.StateChanging);

    public static GitCommandDefinition BranchRename(string oldName, string newName) =>
        new(GitCommandKind.BranchRename, "branch", ["branch", "-m", oldName, newName], GitOperationRisk.Safe);

    public static GitCommandDefinition BranchDelete(string name, bool force) =>
        new(GitCommandKind.BranchDelete, "branch", ["branch", force ? "-D" : "-d", name], GitOperationRisk.Destructive);

    public static GitCommandDefinition SetUpstream(string branch, string upstream) =>
        new(GitCommandKind.SetUpstream, "branch", ["branch", "--set-upstream-to", upstream, branch], GitOperationRisk.Safe);

    public static GitCommandDefinition Merge(string branch, bool noFastForward)
    {
        var args = new List<string> { "merge" };
        if (noFastForward) args.Add("--no-ff");
        args.Add(branch);
        return new(GitCommandKind.Merge, "merge", args, GitOperationRisk.StateChanging);
    }

    public static GitCommandDefinition MergeAbort() =>
        new(GitCommandKind.MergeAbort, "merge", ["merge", "--abort"], GitOperationRisk.Safe);

    // ---- history & inspection ----

    public static GitCommandDefinition Log(int limit, string? path = null)
    {
        var args = new List<string> { "log", $"--format={LogFormat}", $"--max-count={Math.Max(1, limit)}" };
        if (!string.IsNullOrWhiteSpace(path)) { args.Add("--"); args.Add(path); }
        return new(GitCommandKind.Log, "log", args, GitOperationRisk.ReadOnly);
    }

    public static GitCommandDefinition CommitDetail(string sha) =>
        new(GitCommandKind.CommitDetail, "log", ["log", "-1", $"--format={LogFormat}", sha], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition Diff(GitDiffSpec spec)
    {
        List<string> args = spec.Source switch
        {
            GitDiffSource.Staged => ["diff", "--no-color", "--cached"],
            GitDiffSource.Commit => ["show", "--no-color", "--format=", "-p", spec.CommitSha ?? "HEAD"],
            _ => ["diff", "--no-color"]
        };
        if (!string.IsNullOrWhiteSpace(spec.Path)) { args.Add("--"); args.Add(spec.Path); }
        var command = spec.Source == GitDiffSource.Commit ? "show" : "diff";
        return new(GitCommandKind.Diff, command, args, GitOperationRisk.ReadOnly);
    }

    // ---- stash ----

    public static GitCommandDefinition StashList() =>
        new(GitCommandKind.StashList, "stash", ["stash", "list", $"--format={StashFormat}"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition StashPush(string? message, bool includeUntracked)
    {
        var args = new List<string> { "stash", "push" };
        if (includeUntracked) args.Add("-u");
        if (!string.IsNullOrWhiteSpace(message)) { args.Add("-m"); args.Add(message); }
        return new(GitCommandKind.StashPush, "stash", args, GitOperationRisk.Safe);
    }

    public static GitCommandDefinition StashApply(int index) =>
        new(GitCommandKind.StashApply, "stash", ["stash", "apply", StashRef(index)], GitOperationRisk.StateChanging);

    public static GitCommandDefinition StashPop(int index) =>
        new(GitCommandKind.StashPop, "stash", ["stash", "pop", StashRef(index)], GitOperationRisk.StateChanging);

    public static GitCommandDefinition StashDrop(int index) =>
        new(GitCommandKind.StashDrop, "stash", ["stash", "drop", StashRef(index)], GitOperationRisk.Destructive);

    // ---- Phase 6: inspection & local history rewriting ----

    public static GitCommandDefinition RevParseGitDir() =>
        new(GitCommandKind.RevParseGitDir, "rev-parse", ["rev-parse", "--absolute-git-dir"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition Reflog(int limit) =>
        new(GitCommandKind.Reflog, "reflog",
            ["reflog", $"--format={ReflogFormat}", $"--max-count={Math.Max(1, limit)}"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition Blame(string path, string? revision = null)
    {
        var args = new List<string> { "blame", "--line-porcelain" };
        if (!string.IsNullOrWhiteSpace(revision)) args.Add(revision);
        args.Add("--");
        args.Add(path);
        return new(GitCommandKind.Blame, "blame", args, GitOperationRisk.ReadOnly);
    }

    /// <summary><c>reset --hard</c> overwrites the working tree → destructive; soft/mixed are reversible.</summary>
    public static GitCommandDefinition Reset(GitResetMode mode, string target)
    {
        var flag = mode switch
        {
            GitResetMode.Soft => "--soft",
            GitResetMode.Hard => "--hard",
            _ => "--mixed"
        };
        var risk = mode == GitResetMode.Hard ? GitOperationRisk.Destructive : GitOperationRisk.StateChanging;
        return new(GitCommandKind.Reset, "reset", ["reset", flag, target], risk);
    }

    public static GitCommandDefinition CherryPick(IReadOnlyList<string> commitIshes)
    {
        var args = new List<string> { "cherry-pick" };
        args.AddRange(commitIshes);
        return new(GitCommandKind.CherryPick, "cherry-pick", args, GitOperationRisk.StateChanging);
    }

    /// <summary><c>revert --no-edit</c> so it never blocks on an editor; still records a new commit per revert.</summary>
    public static GitCommandDefinition Revert(IReadOnlyList<string> commitIshes)
    {
        var args = new List<string> { "revert", "--no-edit" };
        args.AddRange(commitIshes);
        return new(GitCommandKind.Revert, "revert", args, GitOperationRisk.StateChanging);
    }

    /// <summary>Drives the sequencer (cherry-pick/revert/rebase) forward or unwinds it.</summary>
    public static GitCommandDefinition Sequencer(string verb, SequencerAction action)
    {
        var flag = action switch
        {
            SequencerAction.Continue => "--continue",
            SequencerAction.Abort => "--abort",
            SequencerAction.Skip => "--skip",
            _ => "--quit"
        };
        // Abort restores the pre-operation state → safe; continue/skip advance and may re-conflict.
        var risk = action == SequencerAction.Abort ? GitOperationRisk.Safe : GitOperationRisk.StateChanging;
        return new(GitCommandKind.Sequencer, verb, [verb, flag], risk);
    }

    /// <summary>Writes/updates the commit-graph for faster history walks — an on-disk optimisation, no refs move.</summary>
    public static GitCommandDefinition CommitGraphWrite() =>
        new(GitCommandKind.CommitGraph, "commit-graph",
            ["commit-graph", "write", "--reachable", "--changed-paths"], GitOperationRisk.Safe);

    // ---- Phase 6: tags ----

    public static GitCommandDefinition TagList() =>
        new(GitCommandKind.TagList, "for-each-ref",
            ["for-each-ref", $"--format={TagFormat}", "--sort=-creatordate", "refs/tags"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition TagCreate(GitTagRequest req)
    {
        var args = new List<string> { "tag" };
        if (req.Annotated)
        {
            args.Add("-a");
            args.Add(req.Name);
            args.Add("-m");
            args.Add(req.Message ?? req.Name);
        }
        else
        {
            args.Add(req.Name);
        }
        if (!string.IsNullOrWhiteSpace(req.Target)) args.Add(req.Target);
        return new(GitCommandKind.TagCreate, "tag", args, GitOperationRisk.Safe);
    }

    public static GitCommandDefinition TagDelete(string name) =>
        new(GitCommandKind.TagDelete, "tag", ["tag", "-d", name], GitOperationRisk.Destructive);

    public static GitCommandDefinition TagPush(string remote, string name) =>
        new(GitCommandKind.TagPush, "push", ["push", remote, $"refs/tags/{name}"], GitOperationRisk.StateChanging, TargetsRemote: true);

    public static GitCommandDefinition TagPushAll(string remote) =>
        new(GitCommandKind.TagPushAll, "push", ["push", remote, "--tags"], GitOperationRisk.StateChanging, TargetsRemote: true);

    public static GitCommandDefinition TagDeleteRemote(string remote, string name) =>
        new(GitCommandKind.TagDeleteRemote, "push", ["push", remote, "--delete", $"refs/tags/{name}"], GitOperationRisk.Destructive, TargetsRemote: true);

    // ---- Phase 6: worktrees ----

    public static GitCommandDefinition WorktreeList() =>
        new(GitCommandKind.WorktreeList, "worktree", ["worktree", "list", "--porcelain"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition WorktreeAdd(string path, string? branch, bool newBranch)
    {
        var args = new List<string> { "worktree", "add" };
        if (newBranch && !string.IsNullOrWhiteSpace(branch)) { args.Add("-b"); args.Add(branch); }
        args.Add(path);
        if (!newBranch && !string.IsNullOrWhiteSpace(branch)) args.Add(branch);
        return new(GitCommandKind.WorktreeAdd, "worktree", args, GitOperationRisk.StateChanging);
    }

    /// <summary>Removing a worktree with uncommitted changes needs <c>--force</c> → destructive.</summary>
    public static GitCommandDefinition WorktreeRemove(string path, bool force)
    {
        var args = new List<string> { "worktree", "remove" };
        if (force) args.Add("--force");
        args.Add(path);
        return new(GitCommandKind.WorktreeRemove, "worktree", args,
            force ? GitOperationRisk.Destructive : GitOperationRisk.StateChanging);
    }

    public static GitCommandDefinition WorktreePrune() =>
        new(GitCommandKind.WorktreePrune, "worktree", ["worktree", "prune"], GitOperationRisk.Safe);

    // ---- Phase 6: submodules ----

    public static GitCommandDefinition SubmoduleStatus() =>
        new(GitCommandKind.SubmoduleStatus, "submodule", ["submodule", "status"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition SubmoduleUpdate(bool init, bool recursive)
    {
        var args = new List<string> { "submodule", "update" };
        if (init) args.Add("--init");
        if (recursive) args.Add("--recursive");
        return new(GitCommandKind.SubmoduleUpdate, "submodule", args, GitOperationRisk.StateChanging, TargetsRemote: true);
    }

    public static GitCommandDefinition SubmoduleSync(bool recursive)
    {
        var args = new List<string> { "submodule", "sync" };
        if (recursive) args.Add("--recursive");
        return new(GitCommandKind.SubmoduleSync, "submodule", args, GitOperationRisk.Safe);
    }

    // ---- Phase 6: Git LFS (indicators only) ----

    public static GitCommandDefinition LfsVersion() =>
        new(GitCommandKind.LfsStatus, "lfs", ["lfs", "version"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition LfsTrack() =>
        new(GitCommandKind.LfsTrack, "lfs", ["lfs", "track"], GitOperationRisk.ReadOnly);

    // ---- Phase 6: partial / line staging ----

    /// <summary>Applies a reconstructed partial patch to the index; <paramref name="reverse"/> unstages.</summary>
    public static GitCommandDefinition ApplyPatch(string patchFilePath, bool reverse)
    {
        var args = new List<string> { "apply", "--cached", "--whitespace=nowarn" };
        if (reverse) args.Add("--reverse");
        args.Add(patchFilePath);
        return new(GitCommandKind.ApplyPatch, "apply", args, GitOperationRisk.Safe);
    }

    // ---- Phase 6: interactive rebase ----

    /// <summary>Rewrites history from <paramref name="onto"/> — destructive; driven non-interactively via env editors.</summary>
    public static GitCommandDefinition RebaseInteractive(string onto, bool autosquash)
    {
        var args = new List<string> { "rebase", "-i" };
        if (autosquash) args.Add("--autosquash");
        args.Add(onto);
        return new(GitCommandKind.RebaseInteractive, "rebase", args, GitOperationRisk.Destructive);
    }

    // ---- Phase 6: conflict editor ----

    /// <summary>Reads a specific merge stage of a conflicted path (<c>1</c>=base, <c>2</c>=ours, <c>3</c>=theirs).</summary>
    public static GitCommandDefinition ShowStage(int stage, string path) =>
        new(GitCommandKind.ShowObject, "show", ["show", $":{stage}:{path}"], GitOperationRisk.ReadOnly);

    /// <summary>Reads a blob at a revision (<c>&lt;rev&gt;:&lt;path&gt;</c>).</summary>
    public static GitCommandDefinition ShowBlob(string revision, string path) =>
        new(GitCommandKind.ShowObject, "show", ["show", $"{revision}:{path}"], GitOperationRisk.ReadOnly);

    /// <summary>Takes one whole side of a conflict (<c>--ours</c>/<c>--theirs</c>) for a path.</summary>
    public static GitCommandDefinition CheckoutConflictSide(GitConflictSide side, string path) =>
        new(GitCommandKind.Checkout2, "checkout",
            ["checkout", side == GitConflictSide.Ours ? "--ours" : "--theirs", "--", path], GitOperationRisk.StateChanging);

    // ---- helpers ----

    private static string StashRef(int index) => $"stash@{{{index}}}";

    private static IReadOnlyList<string> Concat(IReadOnlyList<string> head, IReadOnlyList<string> tail)
    {
        var list = new List<string>(head.Count + tail.Count);
        list.AddRange(head);
        list.AddRange(tail);
        return list;
    }
}
