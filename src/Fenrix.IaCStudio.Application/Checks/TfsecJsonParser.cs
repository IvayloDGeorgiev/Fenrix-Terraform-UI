using System.Text.Json;
using Fenrix.IaCStudio.Contracts.Checks;

namespace Fenrix.IaCStudio.Application.Checks;

/// <summary>
/// Parses <c>tfsec --format json</c> output into normalised <see cref="CheckFinding"/>s. Pure and defensive.
/// tfsec emits a top-level <c>results</c> array, each with a rule id, severity, description, location, links,
/// and (optionally) the resource address. See docs/34-checks.md.
/// </summary>
public static class TfsecJsonParser
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
                || !root.TryGetProperty("results", out var results)
                || results.ValueKind != JsonValueKind.Array)
                return findings;

            foreach (var r in results.EnumerateArray())
            {
                var ruleId = GetString(r, "rule_id") ?? GetString(r, "long_id") ?? "tfsec";
                var severity = CheckSeverityMap.FromScanner(GetString(r, "severity"));
                var title = GetString(r, "rule_description");
                var message = GetString(r, "description") ?? title ?? "(no message)";
                var resource = GetString(r, "resource");
                var link = FirstLink(r);

                string? file = null;
                int? line = null;
                if (r.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object)
                {
                    file = GetString(loc, "filename");
                    line = GetInt(loc, "start_line");
                }

                findings.Add(new CheckFinding(
                    CheckTool.Tfsec, severity, ruleId, title, message, file, line, resource, link));
            }
        }

        return findings;
    }

    private static string? FirstLink(JsonElement r)
    {
        if (r.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
            foreach (var l in links.EnumerateArray())
                if (l.ValueKind == JsonValueKind.String)
                    return l.GetString();
        return null;
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
