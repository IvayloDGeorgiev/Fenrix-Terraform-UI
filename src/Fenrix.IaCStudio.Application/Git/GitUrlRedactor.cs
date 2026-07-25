using System.Text.RegularExpressions;

namespace Fenrix.IaCStudio.Application.Git;

/// <summary>
/// Redacts credentials embedded in remote URLs so the command preview, copied text, and history never leak
/// a token or password. A URL like <c>https://user:token@host/repo.git</c> becomes
/// <c>https://user:••••@host/repo.git</c>; a bare <c>https://token@host/…</c> becomes
/// <c>https://••••@host/…</c>. Non-URL arguments are returned unchanged. See docs/08-git-engine.md,
/// docs/11-secrets.md and docs/23-command-transparency.md.
/// </summary>
public static partial class GitUrlRedactor
{
    public const string Placeholder = "••••";

    // scheme://userinfo@host…  — userinfo is "user[:password]" or a bare token.
    [GeneratedRegex(@"^(?<scheme>[a-zA-Z][a-zA-Z0-9+.\-]*://)(?<user>[^/@:]+)?(?::(?<pass>[^/@]*))?@(?<rest>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex UrlWithUserInfo();

    /// <summary>True when the argument looks like a remote URL carrying userinfo credentials.</summary>
    public static bool LooksLikeCredentialedUrl(string argument) =>
        !string.IsNullOrEmpty(argument) && UrlWithUserInfo().IsMatch(argument);

    /// <summary>Returns the argument with any embedded credential replaced by a placeholder.</summary>
    public static string Redact(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return argument;

        var m = UrlWithUserInfo().Match(argument);
        if (!m.Success)
            return argument;

        var scheme = m.Groups["scheme"].Value;
        var rest = m.Groups["rest"].Value;

        // Bare token in the user position (no password) → redact the whole userinfo.
        if (!m.Groups["pass"].Success)
        {
            var user = m.Groups["user"].Value;
            // A username with no password is usually not a secret (e.g. "git@"); keep well-known names,
            // redact anything long/token-like to be safe.
            var keepUser = user is "git" or "" || user.Length <= 24;
            return keepUser && user.Length > 0
                ? argument
                : $"{scheme}{Placeholder}@{rest}";
        }

        // user:password → keep the username, redact the password.
        var name = m.Groups["user"].Success ? m.Groups["user"].Value : string.Empty;
        return $"{scheme}{name}:{Placeholder}@{rest}";
    }
}
