using System.Text;
using System.Text.RegularExpressions;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Pure parsers for Terraform's own help text (Phase 12 dynamic command builder). Turns the top-level
/// <c>terraform -help</c> listing into a command list, and a per-command <c>terraform &lt;cmd&gt; -help</c>
/// dump into a synopsis, usage line, and typed flags. Kept dependency-free and side-effect-free so it can be
/// unit-tested against captured fixtures; the Infrastructure service supplies the text. See
/// docs/05-terraform-engine.md.
/// </summary>
public static partial class TerraformHelpParser
{
    // A command row in the top-level listing:  "    plan          Show changes required by the current configuration"
    [GeneratedRegex(@"^\s{2,}([a-z][a-z0-9-]*)\s{2,}(\S.*)$")]
    private static partial Regex CommandRow();

    // A flag row in a per-command help dump:  "  -upgrade            Install the latest module and provider versions"
    //                                          "  -var-file=path      ..."   "  -target=resource    ..."
    [GeneratedRegex(@"^\s{2,}-([A-Za-z][A-Za-z0-9-]*)(=(\S+))?\b(.*)$")]
    private static partial Regex FlagRow();

    /// <summary>
    /// Parses the command list from <c>terraform -help</c>. Commands under a heading containing "other" (i.e.
    /// "All other commands:") are marked non-common. Robust to spacing and section order.
    /// </summary>
    public static IReadOnlyList<TerraformCommandInfo> ParseCommandList(string helpText)
    {
        var results = new List<TerraformCommandInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var common = true;

        foreach (var raw in SplitLines(helpText))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0) continue;

            // Section headings flip the "common" flag once we pass the main commands.
            var trimmed = line.TrimStart();
            if (!line.StartsWith(' ') || (trimmed.EndsWith(':') && !trimmed.StartsWith('-')))
            {
                if (trimmed.Contains("other", StringComparison.OrdinalIgnoreCase))
                    common = false;
                continue;
            }

            var m = CommandRow().Match(line);
            if (!m.Success) continue;

            var name = m.Groups[1].Value;
            // Skip words that are obviously not commands (help sometimes indents prose).
            if (name is "the" or "and" or "for" or "with") continue;
            if (!seen.Add(name)) continue;

            var synopsis = m.Groups[2].Value.Trim();
            var redirect = TerraformCommandClassifier.RedirectFor([name]);
            results.Add(new TerraformCommandInfo(
                name, synopsis, common,
                redirect is not null, redirect?.Reason, redirect?.TargetRoute));
        }

        return results.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Parses a per-command <c>-help</c> dump into a synopsis, usage line, and flags.</summary>
    public static TerraformCommandHelp ParseCommandHelp(string command, string helpText)
    {
        string? usage = null;
        var synopsis = new StringBuilder();
        var flags = new List<TerraformFlagInfo>();
        var seenFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var inOptions = false;

        foreach (var raw in SplitLines(helpText))
        {
            var line = raw.TrimEnd();
            var trimmed = line.Trim();

            if (trimmed.StartsWith("Usage:", StringComparison.OrdinalIgnoreCase))
            {
                usage = trimmed;
                continue;
            }

            if (trimmed.StartsWith("Options", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("Flags", StringComparison.OrdinalIgnoreCase))
            {
                inOptions = true;
                continue;
            }

            var fm = FlagRow().Match(line);
            if (fm.Success)
            {
                inOptions = true;
                var name = fm.Groups[1].Value;
                if (!seenFlags.Add(name)) continue;

                var hasValueToken = fm.Groups[2].Success;                 // "-flag=path"
                var valueHint = fm.Groups[3].Success ? fm.Groups[3].Value : null;
                var desc = fm.Groups[4].Value.Trim();
                // Some flags show the value as a separate word after spaces rather than "=value".
                flags.Add(new TerraformFlagInfo(name, hasValueToken, desc, valueHint));
                continue;
            }

            // Before the options section, non-empty, non-usage lines form the synopsis.
            if (!inOptions && trimmed.Length > 0 && !trimmed.StartsWith('-'))
            {
                if (synopsis.Length > 0) synopsis.Append(' ');
                synopsis.Append(trimmed);
            }
        }

        return new TerraformCommandHelp(
            command,
            synopsis.ToString().Trim(),
            usage,
            flags.OrderBy(f => f.Name, StringComparer.Ordinal).ToList(),
            helpText);
    }

    private static IEnumerable<string> SplitLines(string text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
}
