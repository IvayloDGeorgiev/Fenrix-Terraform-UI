using Fenrix.IaCStudio.Contracts.Checks;

namespace Fenrix.IaCStudio.Application.Checks;

/// <summary>
/// Maps each tool's native severity strings onto the normalised <see cref="CheckSeverity"/> scale so findings
/// from different tools sort and filter consistently. Pure and side-effect free. See docs/34-checks.md.
/// </summary>
public static class CheckSeverityMap
{
    /// <summary>tfsec / Trivy use CRITICAL/HIGH/MEDIUM/LOW/UNKNOWN.</summary>
    public static CheckSeverity FromScanner(string? severity) => Normalize(severity) switch
    {
        "critical" => CheckSeverity.Critical,
        "high" => CheckSeverity.High,
        "medium" => CheckSeverity.Medium,
        "low" => CheckSeverity.Low,
        "info" or "informational" => CheckSeverity.Info,
        _ => CheckSeverity.Unknown
    };

    /// <summary>TFLint uses error/warning/notice (older builds: info).</summary>
    public static CheckSeverity FromTfLint(string? severity) => Normalize(severity) switch
    {
        "error" => CheckSeverity.High,
        "warning" => CheckSeverity.Medium,
        "notice" or "info" => CheckSeverity.Info,
        _ => CheckSeverity.Unknown
    };

    /// <summary>A short human label for a normalised severity.</summary>
    public static string Label(CheckSeverity severity) => severity switch
    {
        CheckSeverity.Critical => "Critical",
        CheckSeverity.High => "High",
        CheckSeverity.Medium => "Medium",
        CheckSeverity.Low => "Low",
        CheckSeverity.Info => "Info",
        _ => "Unknown"
    };

    private static string Normalize(string? s) => (s ?? string.Empty).Trim().ToLowerInvariant();
}
