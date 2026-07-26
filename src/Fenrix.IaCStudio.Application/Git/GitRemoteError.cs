namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Classifies a failed remote Git operation's output and produces actionable guidance. Fenrix runs remote
/// commands non-interactively (<c>GIT_TERMINAL_PROMPT=0</c>) against the OS credential store, so an auth
/// failure surfaces as a fast error rather than an interactive prompt; this turns that terse output into a
/// clear next step. See docs/08-git-engine.md and docs/16-error-handling.md.
/// </summary>
public static class GitRemoteError
{
    private static readonly string[] AuthSignatures =
    [
        "authentication failed",
        "could not read username",
        "could not read password",
        "terminal prompts disabled",
        "invalid username or password",
        "support for password authentication was removed",
        "permission denied (publickey)",
        "fatal: authentication",
        "remote: invalid",
        "403 forbidden",
        "401 unauthorized"
    ];

    private static readonly string[] NotFoundSignatures =
    [
        "repository not found",
        "does not exist",
        "could not read from remote repository"
    ];

    /// <summary>True when the output looks like an authentication/authorization failure.</summary>
    public static bool IsAuthFailure(string? output) =>
        output is not null && AuthSignatures.Any(s => output.Contains(s, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns friendly guidance to append when a remote op fails, or null when the failure isn't a
    /// recognised auth/access problem (leave the original error as-is).
    /// </summary>
    public static string? Guidance(string? output)
    {
        if (output is null)
            return null;

        if (IsAuthFailure(output))
            return "Authentication to the remote failed. Fenrix runs Git non-interactively against your OS " +
                   "credential store (Git Credential Manager). Sign in there — e.g. run a manual `git fetch` " +
                   "once so GCM can prompt — or add an access token to this project's repository connection " +
                   "(Source control → Provider). For SSH remotes, make sure your key is loaded in the agent.";

        if (NotFoundSignatures.Any(s => output.Contains(s, StringComparison.OrdinalIgnoreCase)))
            return "The remote repository couldn't be read. It may not exist, or the stored credentials may " +
                   "not have access to it. Check the remote URL and your credentials, then retry.";

        return null;
    }
}
