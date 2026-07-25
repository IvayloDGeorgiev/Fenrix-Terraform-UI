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

    // ---- repository ----

    public static GitCommandDefinition Version() =>
        new(GitCommandKind.Version, "version", ["--version"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition Init() =>
        new(GitCommandKind.Init, "init", ["init"], GitOperationRisk.Safe);

    public static GitCommandDefinition Clone(string url, string folderName) =>
        new(GitCommandKind.Clone, "clone", ["clone", "--progress", url, folderName],
            GitOperationRisk.StateChanging, TargetsRemote: true);

    public static GitCommandDefinition RevParseTopLevel() =>
        new(GitCommandKind.RevParseTopLevel, "rev-parse", ["rev-parse", "--show-toplevel"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition RevParseHead() =>
        new(GitCommandKind.RevParseTopLevel, "rev-parse", ["rev-parse", "HEAD"], GitOperationRisk.ReadOnly);

    public static GitCommandDefinition CurrentBranch() =>
        new(GitCommandKind.RevParseTopLevel, "rev-parse", ["rev-parse", "--abbrev-ref", "HEAD"], GitOperationRisk.ReadOnly);

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
