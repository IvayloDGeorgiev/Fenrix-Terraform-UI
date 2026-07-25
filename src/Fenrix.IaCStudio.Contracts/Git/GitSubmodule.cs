namespace Fenrix.IaCStudio.Contracts.Git;

/// <summary>
/// State of a submodule from <c>git submodule status</c>: its path, the recorded commit, and a status flag
/// derived from the leading marker — <c>' '</c> in sync, <c>'-'</c> not initialised, <c>'+'</c> checked out
/// at a different commit, <c>'U'</c> has merge conflicts. See docs/08-git-engine.md.
/// </summary>
public sealed record GitSubmodule(
    string Path,
    string Sha,
    GitSubmoduleState State,
    string? Describe);

/// <summary>The initialisation/sync state of a submodule, from the leading marker of <c>git submodule status</c>.</summary>
public enum GitSubmoduleState
{
    /// <summary>Initialised and at the recorded commit (leading space).</summary>
    InSync = 0,

    /// <summary>Not initialised (leading '-').</summary>
    Uninitialised = 1,

    /// <summary>Checked out at a different commit than recorded (leading '+').</summary>
    OutOfSync = 2,

    /// <summary>Has merge conflicts (leading 'U').</summary>
    Conflicted = 3
}
