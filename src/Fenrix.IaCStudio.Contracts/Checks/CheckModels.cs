namespace Fenrix.IaCStudio.Contracts.Checks;

/// <summary>
/// The external analysis tools Fenrix can run over an environment's working directory (Phase 13 · Checks).
/// Each is a standalone binary driven through the shared <c>ArgumentList</c> process runner — never a shell
/// string — with its JSON output parsed in memory into <see cref="CheckFinding"/>s. See docs/34-checks.md.
/// </summary>
public enum CheckTool
{
    /// <summary>TFLint — linting, deprecations, provider-specific best-practice rules.</summary>
    TfLint = 0,
    /// <summary>tfsec — security misconfiguration scanner (now folded into Trivy upstream).</summary>
    Tfsec = 1,
    /// <summary>Trivy (<c>trivy config</c>) — security/misconfiguration scanner, tfsec's successor.</summary>
    Trivy = 2,
    /// <summary>Infracost — cloud cost estimation (Phase 13 · cost, see docs/34-checks.md).</summary>
    Infracost = 3
}

/// <summary>
/// A normalised severity across every check tool. Ordered so a larger value is more serious, which lets the
/// UI filter "this severity and above". Each tool's native levels are mapped onto this scale by
/// <c>CheckSeverityMap</c>. See docs/34-checks.md.
/// </summary>
public enum CheckSeverity
{
    Unknown = 0,
    Info = 1,
    Low = 2,
    Medium = 3,
    High = 4,
    Critical = 5
}

/// <summary>
/// One finding from a check tool: a rule that fired at an optional file/line, normalised across tools so the
/// UI can show and filter them uniformly. Findings never carry secret values — only rule metadata, a message,
/// and a source location. See docs/34-checks.md.
/// </summary>
/// <param name="Tool">The tool that produced the finding.</param>
/// <param name="Severity">Normalised severity.</param>
/// <param name="RuleId">The tool's rule identifier (e.g. <c>aws-s3-enable-bucket-encryption</c>, <c>terraform_deprecated_syntax</c>).</param>
/// <param name="Title">A short rule title, when the tool provides one.</param>
/// <param name="Message">The human-readable finding message.</param>
/// <param name="FilePath">The offending file, relative to the working directory when resolvable.</param>
/// <param name="Line">The 1-based start line, when the tool reports one.</param>
/// <param name="Resource">The Terraform resource address the finding relates to, when available.</param>
/// <param name="Link">A documentation URL for the rule, when the tool provides one.</param>
public sealed record CheckFinding(
    CheckTool Tool,
    CheckSeverity Severity,
    string RuleId,
    string? Title,
    string Message,
    string? FilePath,
    int? Line,
    string? Resource,
    string? Link);

/// <summary>
/// The outcome of running one static-analysis tool over an environment. Distinguishes "tool not installed"
/// from "ran and found nothing" from "ran and failed" so the UI can guide the user precisely. See docs/34-checks.md.
/// </summary>
/// <param name="Tool">Which tool this result is for.</param>
/// <param name="Available">True when a binary was resolved for the tool.</param>
/// <param name="Ran">True when the tool actually executed (a process was launched).</param>
/// <param name="ExitCode">The process exit code (findings-present is a non-zero exit for most tools, so this is informational only).</param>
/// <param name="Findings">The parsed findings (may be empty).</param>
/// <param name="Cancelled">True when the run was cancelled.</param>
/// <param name="Error">A human-readable failure reason when the tool could not be run or its output could not be parsed. Never a secret.</param>
public sealed record CheckToolRun(
    CheckTool Tool,
    bool Available,
    bool Ran,
    int ExitCode,
    IReadOnlyList<CheckFinding> Findings,
    bool Cancelled,
    string? Error)
{
    public static CheckToolRun NotAvailable(CheckTool tool) =>
        new(tool, false, false, -1, [], false, null);
}

/// <summary>
/// The aggregated static-analysis report for an environment: one <see cref="CheckToolRun"/> per tool plus the
/// flattened, severity-sorted finding list. See docs/34-checks.md.
/// </summary>
public sealed record StaticAnalysisReport(
    IReadOnlyList<CheckToolRun> Runs,
    IReadOnlyList<CheckFinding> Findings)
{
    public int CountAtLeast(CheckSeverity minimum) => Findings.Count(f => f.Severity >= minimum);

    public int Count(CheckSeverity severity) => Findings.Count(f => f.Severity == severity);

    public static readonly StaticAnalysisReport Empty = new([], []);
}
