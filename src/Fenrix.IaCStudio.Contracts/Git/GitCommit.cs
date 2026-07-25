namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// One commit from <c>git log</c>, parsed from an explicit NUL-delimited <c>--format</c> so subjects and
/// bodies with arbitrary punctuation stay intact. See docs/08-git-engine.md.
/// </summary>
public sealed record GitCommit(
    string Sha,
    string ShortSha,
    string Author,
    string Email,
    DateTimeOffset Date,
    IReadOnlyList<string> Parents,
    string Subject,
    string Body)
{
    public bool IsMerge => Parents.Count > 1;
}
