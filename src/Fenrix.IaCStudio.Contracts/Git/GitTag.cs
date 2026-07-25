namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// A Git tag parsed from <c>git for-each-ref refs/tags</c>: its name, whether it is annotated (a real tag
/// object) or lightweight (a direct ref to a commit), the commit it ultimately points at, when it was
/// created, and the annotation subject for annotated tags. See docs/08-git-engine.md.
/// </summary>
public sealed record GitTag(
    string Name,
    bool IsAnnotated,
    string TargetSha,
    DateTimeOffset Date,
    string? Subject);

/// <summary>What the user wants to create: a lightweight or annotated tag, optionally at a specific commit.</summary>
public sealed record GitTagRequest(
    string Name,
    bool Annotated,
    string? Message = null,
    string? Target = null);
