using Fenrix.IaCStudio.Domain.Git;

namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// Whether a multi-step Git operation (merge, cherry-pick, revert, rebase) is currently paused in the
/// working tree, plus any conflicted paths. Drives the continue / abort / skip banners so the user is never
/// stranded mid-sequence. Derived from the marker files under the git directory. See docs/08-git-engine.md.
/// </summary>
public sealed record GitSequencerState(
    GitSequencerOperation Operation,
    bool HasConflicts,
    IReadOnlyList<string> ConflictedPaths,
    string? HeadSha,
    string? OntoLabel)
{
    public static GitSequencerState Idle { get; } = new(GitSequencerOperation.None, false, [], null, null);

    public bool InProgress => Operation != GitSequencerOperation.None;
}
