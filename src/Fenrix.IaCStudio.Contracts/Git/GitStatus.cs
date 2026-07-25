using Fenrix.IaCStudio.Domain.Git;

namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// One changed path from <c>git status --porcelain=v2 -z</c>: its staged (index) and unstaged
/// (working-tree) states, plus derived flags the UI groups by. Untracked ('?') and ignored ('!') records
/// are surfaced here too, with the matching state on the working-tree side. See docs/08-git-engine.md.
/// </summary>
public sealed record GitStatusEntry(
    string Path,
    string? OriginalPath,
    GitChangeState IndexState,
    GitChangeState WorkTreeState,
    bool IsConflicted,
    bool IsUntracked,
    bool IsIgnored,
    int? RenameScore)
{
    /// <summary>Has a staged change to include in the next commit.</summary>
    public bool IsStaged =>
        !IsConflicted && !IsUntracked && !IsIgnored &&
        IndexState is not (GitChangeState.Unmodified or GitChangeState.Untracked or GitChangeState.Ignored);

    /// <summary>Has an unstaged working-tree change (or is untracked).</summary>
    public bool IsUnstaged =>
        !IsConflicted &&
        (IsUntracked ||
         WorkTreeState is not (GitChangeState.Unmodified or GitChangeState.Untracked or GitChangeState.Ignored));

    /// <summary>Short two-letter code (e.g. "M.", "R.", ".M", "??", "UU") for compact display.</summary>
    public string Code =>
        IsUntracked ? "??" :
        IsIgnored ? "!!" :
        IsConflicted ? "UU" :
        $"{Letter(IndexState)}{Letter(WorkTreeState)}";

    private static char Letter(GitChangeState s) => s switch
    {
        GitChangeState.Unmodified => '.',
        GitChangeState.Modified => 'M',
        GitChangeState.Added => 'A',
        GitChangeState.Deleted => 'D',
        GitChangeState.Renamed => 'R',
        GitChangeState.Copied => 'C',
        GitChangeState.TypeChanged => 'T',
        GitChangeState.Unmerged => 'U',
        GitChangeState.Untracked => '?',
        GitChangeState.Ignored => '!',
        _ => '.'
    };
}

/// <summary>
/// A parsed working-copy status: branch/upstream context, ahead/behind counts, and the changed entries.
/// Grouping helpers give the UI its staged / unstaged / untracked / conflicted lists. See
/// docs/08-git-engine.md.
/// </summary>
public sealed record GitStatus(
    bool IsRepository,
    string? Branch,
    string? Oid,
    string? Upstream,
    int Ahead,
    int Behind,
    bool IsDetached,
    IReadOnlyList<GitStatusEntry> Entries)
{
    public static GitStatus NotARepository { get; } =
        new(false, null, null, null, 0, 0, false, []);

    public IEnumerable<GitStatusEntry> Conflicted => Entries.Where(e => e.IsConflicted);
    public IEnumerable<GitStatusEntry> Staged => Entries.Where(e => e.IsStaged);
    public IEnumerable<GitStatusEntry> Unstaged => Entries.Where(e => e.IsUnstaged && !e.IsUntracked);
    public IEnumerable<GitStatusEntry> Untracked => Entries.Where(e => e.IsUntracked);

    /// <summary>True when there is anything to stage, commit or resolve.</summary>
    public bool HasChanges => Entries.Count > 0;

    /// <summary>True when a merge/rebase left conflicted paths in the working tree.</summary>
    public bool HasConflicts => Entries.Any(e => e.IsConflicted);

    public bool HasUpstream => !string.IsNullOrEmpty(Upstream);
}
