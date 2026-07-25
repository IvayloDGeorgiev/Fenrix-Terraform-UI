namespace Fenrix.IaCStudio.Domain.Git;

/// <summary>
/// How risky a Git operation is, used to colour the command preview and gate confirmations. Mirrors the
/// Terraform risk ladder so the shared preview UI can treat both tools the same. See
/// docs/08-git-engine.md and docs/23-command-transparency.md.
/// </summary>
public enum GitOperationRisk
{
    /// <summary>Reads only — status, log, diff, branch listing, rev-parse.</summary>
    ReadOnly = 0,

    /// <summary>Changes the repository index/refs locally but is easily reversible — stage, commit, branch, stash.</summary>
    Safe = 1,

    /// <summary>Touches a remote or moves HEAD across commits — fetch, pull, push, checkout, merge.</summary>
    StateChanging = 2,

    /// <summary>Can lose work — discard, hard reset, force push, branch delete, clean.</summary>
    Destructive = 3
}
