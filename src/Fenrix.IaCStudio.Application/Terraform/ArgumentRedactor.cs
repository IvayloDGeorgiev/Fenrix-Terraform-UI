namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Redacts secrets from command arguments and environment variables so previews, copied text, and
/// history never leak sensitive values. Redaction is conservative: any assignment whose key looks
/// credential-like has its value replaced with a bullet placeholder. See docs/11-secrets.md and
/// docs/23-command-transparency.md.
/// </summary>
public static class ArgumentRedactor
{
    public const string Placeholder = "••••";

    // Substrings that mark a name/key as credential-bearing (case-insensitive).
    private static readonly string[] SensitiveKeywords =
    [
        "SECRET", "PASSWORD", "PASSWD", "TOKEN", "CREDENTIAL", "APIKEY", "API_KEY",
        "ACCESS_KEY", "SECRET_KEY", "PRIVATE_KEY", "SESSION", "PASSPHRASE", "KEY"
    ];

    /// <summary>True when an environment-variable name should be shown as a named reference only.</summary>
    public static bool IsSensitiveEnvironmentVariable(string name) => ContainsSensitiveKeyword(name);

    /// <summary>Redacts a single argument's value when its key looks sensitive; otherwise returns it unchanged.</summary>
    public static string RedactArgument(string argument)
    {
        if (string.IsNullOrEmpty(argument))
            return argument;

        var eq = argument.LastIndexOf('=');
        if (eq <= 0 || eq == argument.Length - 1)
            return argument;

        var keyPart = argument[..eq];
        return ContainsSensitiveKeyword(keyPart)
            ? string.Concat(argument.AsSpan(0, eq + 1), Placeholder)
            : argument;
    }

    /// <summary>Redacts every argument in a list, preserving order.</summary>
    public static IReadOnlyList<string> RedactArguments(IReadOnlyList<string> arguments)
    {
        var result = new string[arguments.Count];
        for (var i = 0; i < arguments.Count; i++)
            result[i] = RedactArgument(arguments[i]);
        return result;
    }

    private static bool ContainsSensitiveKeyword(string text)
    {
        foreach (var keyword in SensitiveKeywords)
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
