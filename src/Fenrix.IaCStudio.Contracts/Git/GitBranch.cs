namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// A local or remote-tracking branch with its upstream and ahead/behind counts, parsed from
/// <c>git branch --format=…</c>. See docs/08-git-engine.md.
/// </summary>
public sealed record GitBranch(
    string Name,
    string FullName,
    bool IsCurrent,
    bool IsRemote,
    string? Upstream,
    int Ahead,
    int Behind,
    string? Tip,
    string? Subject)
{
    public bool HasUpstream => !string.IsNullOrEmpty(Upstream);
}
