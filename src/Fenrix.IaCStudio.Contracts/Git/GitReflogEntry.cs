namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// One entry from <c>git reflog</c>: the selector (<c>HEAD@{n}</c>), the commit it pointed at, and the
/// reflog subject split into an action (e.g. <c>commit</c>, <c>checkout</c>, <c>reset</c>, <c>rebase</c>)
/// and its description. The reflog is the safety net for recovering commits after a reset/rebase, so the UI
/// surfaces it for "undo". See docs/08-git-engine.md.
/// </summary>
public sealed record GitReflogEntry(
    string Selector,
    string Sha,
    string ShortSha,
    string Action,
    string Description,
    string Author,
    DateTimeOffset Date);
