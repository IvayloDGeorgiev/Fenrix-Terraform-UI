using Fenrix.IaCStudio.Domain.Common;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Pure classification of a Terraform subcommand for the dynamic command builder (Phase 12). Decides which
/// commands the builder may run directly and which must be redirected to their dedicated, safety-gated screens
/// so the saved-plan-only-apply rule (ADR-0003), per-environment locking, and typed confirmations are never
/// bypassed by an ad-hoc run. Also gives a conservative risk level for a custom run.
///
/// <para>The redirect targets are project-relative routes (the builder page fills in the project id).</para>
/// See docs/05-terraform-engine.md, docs/06-plan-apply-safety.md, docs/23-command-transparency.md.
/// </summary>
public static class TerraformCommandClassifier
{
    /// <summary>
    /// Subcommands (and "subcommand verb" pairs) that change real infrastructure or state, or that Fenrix
    /// already exposes through a dedicated safe flow. The builder blocks these and points at the right screen.
    /// Keyed by the first token; a set of blocked second tokens narrows it where only some verbs are mutating
    /// (e.g. <c>state list</c> is fine but <c>state mv/rm/push/replace-provider</c> are not).
    /// </summary>
    private static readonly Dictionary<string, RedirectInfo> Redirects = new(StringComparer.OrdinalIgnoreCase)
    {
        ["apply"]        = new("Apply runs real changes — generate and review a saved plan first.", "plan"),
        ["destroy"]      = new("Destroy is guarded — use the Plan & apply screen's Destroy flow.", "plan"),
        ["import"]       = new("Import changes state — use the Inspect screen's Import assistant.", "inspect"),
        ["force-unlock"] = new("Force-unlock is a guarded state operation — use the Inspect screen.", "inspect"),
        ["taint"]        = new("Taint changes state — model the replacement through Plan & apply instead.", "plan"),
        ["untaint"]      = new("Untaint changes state — use Plan & apply.", "plan"),
        ["login"]        = new("Interactive login is best run in the embedded Terminal.", "terminal"),
        ["logout"]       = new("Interactive logout is best run in the embedded Terminal.", "terminal"),
    };

    // For multi-verb commands, only these (command, verb) pairs are mutating; other verbs are allowed.
    private static readonly HashSet<(string, string)> MutatingVerbs = new(new TupleComparer())
    {
        ("state", "mv"), ("state", "rm"), ("state", "push"), ("state", "replace-provider"),
        ("workspace", "new"), ("workspace", "delete"), ("workspace", "select"),
    };

    private static readonly Dictionary<string, string> VerbRedirect = new(StringComparer.OrdinalIgnoreCase)
    {
        ["state"]     = "inspect",
        ["workspace"] = "inspect",
    };

    public readonly record struct RedirectInfo(string Reason, string TargetRoute);

    /// <summary>
    /// Returns a redirect when the given argument list targets a mutating/guarded command, or null when the
    /// builder may run it directly. <paramref name="arguments"/> is subcommand-first.
    /// </summary>
    public static RedirectInfo? RedirectFor(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
            return null;

        // Asking for help never runs the command (`terraform apply -help` applies nothing), so it is always
        // allowed even for mutating commands — the builder fetches help for every command, blocked or not.
        if (arguments.Any(a => a is "-help" or "--help" or "-h"))
            return null;

        var command = arguments[0];
        if (Redirects.TryGetValue(command, out var direct))
            return direct;

        // Multi-verb command: inspect the first non-flag token after the subcommand.
        var verb = arguments.Skip(1).FirstOrDefault(a => !a.StartsWith('-'));
        if (verb is not null && MutatingVerbs.Contains((command, verb)))
        {
            var route = VerbRedirect.TryGetValue(command, out var r) ? r : "inspect";
            return new RedirectInfo($"'{command} {verb}' changes state — use the Inspect screen's guarded flow.", route);
        }

        return null;
    }

    /// <summary>True when the builder should refuse to run this command directly (a redirect exists).</summary>
    public static bool IsBlocked(IReadOnlyList<string> arguments) => RedirectFor(arguments) is not null;

    /// <summary>
    /// A conservative risk level for a custom (builder-run) command, based on the subcommand. Blocked commands
    /// never reach execution, so anything runnable here is read-only or safe; a small allow-list marks the
    /// filesystem-writing ones as Safe, the rest ReadOnly.
    /// </summary>
    public static TerraformRiskLevel RiskFor(IReadOnlyList<string> arguments)
    {
        if (arguments.Count == 0)
            return TerraformRiskLevel.ReadOnly;

        var command = arguments[0].ToLowerInvariant();
        return command switch
        {
            // Download providers/modules, write .terraform.lock.hcl, rewrite files — no real infrastructure.
            "init" or "get" or "fmt" or "providers" => TerraformRiskLevel.Safe,
            // plan writes only a plan file when -out is given; without it, it changes nothing.
            "plan" => TerraformRiskLevel.Safe,
            _ => TerraformRiskLevel.ReadOnly,
        };
    }

    private sealed class TupleComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(
                obj.Item1.ToLowerInvariant(),
                obj.Item2.ToLowerInvariant());
    }
}
