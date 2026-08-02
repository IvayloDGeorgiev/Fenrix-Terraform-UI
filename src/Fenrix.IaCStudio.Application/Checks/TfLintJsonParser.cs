using System.Text.Json;
using Fenrix.IaCStudio.Contracts.Checks;

namespace Fenrix.IaCStudio.Application.Checks;

/// <summary>
/// Parses <c>tflint --format json</c> output into normalised <see cref="CheckFinding"/>s. Pure and defensive —
/// tolerant of missing fields and of the top-level <c>errors</c> array TFLint emits for config problems.
/// See docs/34-checks.md.
/// </summary>
public static class TfLintJsonParser
{
    public static IReadOnlyList<CheckFinding> Parse(string json)
    {
        var findings = new List<CheckFinding>();
        if (string.IsNullOrWhiteSpace(json)) return findings;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return findings; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return findings;

            if (root.TryGetProperty("issues", out var issues) && issues.ValueKind == JsonValueKind.Array)
            {
                foreach (var issue in issues.EnumerateArray())
                    findings.Add(ParseIssue(issue));
            }

            // TFLint reports configuration/parse problems as top-level errors; surface them as High findings.
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var err in errors.EnumerateArray())
                {
                    var message = GetString(err, "message") ?? GetString(err, "summary") ?? "TFLint error";
                    string? file = null;
                    int? line = null;
                    if (err.TryGetProperty("range", out var range))
                        (file, line) = ParseRange(range);
                    findings.Add(new CheckFinding(
                        CheckTool.TfLint, CheckSeverity.High, "tflint_error", "TFLint error",
                        message, file, line, null, null));
                }
            }
        }

        return findings;
    }

    private static CheckFinding ParseIssue(JsonElement issue)
    {
        string ruleId = "tflint";
        string? title = null;
        string? link = null;
        var severity = CheckSeverity.Unknown;

        if (issue.TryGetProperty("rule", out var rule) && rule.ValueKind == JsonValueKind.Object)
        {
            ruleId = GetString(rule, "name") ?? ruleId;
            link = GetString(rule, "link");
            severity = CheckSeverityMap.FromTfLint(GetString(rule, "severity"));
        }

        var message = GetString(issue, "message") ?? "(no message)";

        string? file = null;
        int? line = null;
        if (issue.TryGetProperty("range", out var range))
            (file, line) = ParseRange(range);

        return new CheckFinding(CheckTool.TfLint, severity, ruleId, title, message, file, line, null, link);
    }

    private static (string? File, int? Line) ParseRange(JsonElement range)
    {
        var file = GetString(range, "filename");
        int? line = null;
        if (range.TryGetProperty("start", out var start) && start.ValueKind == JsonValueKind.Object
            && start.TryGetProperty("line", out var l) && l.ValueKind == JsonValueKind.Number
            && l.TryGetInt32(out var lineVal))
            line = lineVal;
        return (file, line);
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;
}
