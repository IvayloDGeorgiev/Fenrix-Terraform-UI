namespace Fenrix.IaCStudio.Domain.Git;

/// <summary>
/// The control verb applied to an in-progress sequencer operation (cherry-pick, revert, or rebase):
/// <c>--continue</c> after resolving, <c>--abort</c> to unwind to the pre-operation state, <c>--skip</c> to
/// drop the current commit, or <c>--quit</c> to stop without changing HEAD. See docs/08-git-engine.md.
/// </summary>
public enum SequencerAction
{
    Continue = 0,
    Abort = 1,
    Skip = 2,
    Quit = 3
}
