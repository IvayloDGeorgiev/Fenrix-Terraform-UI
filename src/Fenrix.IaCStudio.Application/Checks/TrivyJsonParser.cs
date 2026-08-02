using System.Text.Json;
using Fenrix.IaCStudio.Contracts.Checks;

namespace Fenrix.IaCStudio.Application.Checks;

/// <summary>
/// Parses <c>trivy config --format json</c> output into normalised <see cref="CheckFinding"/>s. Pure and
/// defensive. Trivy nests misconfigurations under <c>Results[].Misconfigurations[]</c>, each with an id,
/// title/description, severity, a primary URL, and cause metadata (resource + start line). Only findings whose
/// status is not <c>PASS</c> are surfaced. See docs/34-checks.md.
/// </summary>
public static class TrivyJsonParser
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
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("Results", out var results)
                || results.ValueKind != JsonValueKind.Array)
                return findings;

            foreach (var result in results.EnumerateArray())
            {
                var target = GetString(result, "Target");
                if (!result.TryGetProperty("Misconfigurations", out var miscs) || miscs.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var m in miscs.EnumerateArray())
                {
                    // Skip anything Trivy reports as passing.
                    if (string.Equals(GetString(m, "Status"), "PASS", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var ruleId = GetString(m, "ID") ?? GetString(m, "AVDID") ?? "trivy";
                    var severity = CheckSeverityMap.FromScanner(GetString(m, "Severity"));
                    var title = GetString(m, "Title");
                    var message = GetString(m, "Message");
                    if (string.IsNullOrWhiteSpace(message)) message = GetString(m, "Description");
                    if (string.IsNullOrWhiteSpace(message)) message = title ?? "(no message)";
                    var link = GetString(m, "PrimaryURL");

                    string? file = target;
                    int? line = null;
                    string? resource = null;
                    if (m.TryGetProperty("CauseMetadata", out var cause) && cause.ValueKind == JsonValueKind.Object)
                    {
                        resource = GetString(cause, "Resource");
                        line = GetInt(cause, "StartLine");
                    }

                    findings.Add(new CheckFinding(
                        CheckTool.Trivy, severity, ruleId, title, message!, file, line, resource, link));
                }
            }
        }

        return findings;
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static int? GetInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)
            ? i
            : null;
}
