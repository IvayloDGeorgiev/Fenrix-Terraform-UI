namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// One entry from <c>git stash list</c>: its index, ref (<c>stash@{n}</c>), the branch it was made on, and
/// the human message. See docs/08-git-engine.md.
/// </summary>
public sealed record GitStash(int Index, string Reference, string? Branch, string Message);
