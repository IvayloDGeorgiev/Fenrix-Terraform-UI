using System.Globalization;
using System.Text.Json;
using Fenrix.IaCStudio.Contracts.Checks;

namespace Fenrix.IaCStudio.Application.Checks;

/// <summary>
/// Parses Infracost <c>breakdown</c> / <c>diff</c> JSON into a <see cref="CostEstimate"/>. Pure and defensive.
/// Infracost reports monetary amounts as invariant-culture strings; a <c>projects[]</c> array carries a
/// <c>breakdown</c> (projected costs) and, for a diff, a <c>diff</c> (per-resource deltas). Top-level totals are
/// <c>totalMonthlyCost</c> and <c>diffTotalMonthlyCost</c>. See docs/34-checks.md.
/// </summary>
public static class InfracostJsonParser
{
    public static CostEstimate Parse(string json, bool asDiff)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new CostEstimate(true, true, asDiff, null, null, null, [], 0, false, "Infracost produced no output.", false);

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException)
        {
            return new CostEstimate(true, true, asDiff, null, null, null, [], 0, false, "Could not parse Infracost output.", false);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var currency = GetString(root, "currency");
            var total = GetDecimal(root, "totalMonthlyCost");
            var totalDelta = asDiff ? GetDecimal(root, "diffTotalMonthlyCost") : null;
            var unsupported = GetUnsupportedCount(root);

            // Projected cost per resource, keyed by address (from the breakdown side).
            var projected = new Dictionary<string, (string? Type, decimal? Cost)>(StringComparer.Ordinal);
            var deltas = new Dictionary<string, decimal?>(StringComparer.Ordinal);
            var order = new List<string>();

            if (root.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
            {
                foreach (var project in projects.EnumerateArray())
                {
                    CollectResources(project, "breakdown", (name, type, cost) =>
                    {
                        if (!projected.ContainsKey(name)) order.Add(name);
                        projected[name] = (type, cost);
                    });

                    if (asDiff)
                        CollectResources(project, "diff", (name, _, cost) =>
                        {
                            if (!projected.ContainsKey(name) && !deltas.ContainsKey(name)) order.Add(name);
                            deltas[name] = cost;
                        });
                }
            }

            var resources = order
                .Select(name =>
                {
                    projected.TryGetValue(name, out var p);
                    decimal? delta = deltas.TryGetValue(name, out var d) ? d : null;
                    return new CostResource(name, p.Type, p.Cost, delta);
                })
                .OrderByDescending(r => asDiff ? (r.MonthlyDelta.HasValue ? Math.Abs(r.MonthlyDelta.Value) : -1) : (r.MonthlyCost ?? -1))
                .ToList();

            return new CostEstimate(
                Available: true, Ran: true, IsDiff: asDiff, Currency: currency,
                TotalMonthlyCost: total, TotalMonthlyDelta: totalDelta,
                Resources: resources, UnsupportedResourceCount: unsupported,
                Cancelled: false, Error: null, NeedsApiKey: false);
        }
    }

    private static void CollectResources(JsonElement project, string section, Action<string, string?, decimal?> add)
    {
        if (!project.TryGetProperty(section, out var s) || s.ValueKind != JsonValueKind.Object) return;
        if (!s.TryGetProperty("resources", out var resources) || resources.ValueKind != JsonValueKind.Array) return;

        foreach (var r in resources.EnumerateArray())
        {
            var name = GetString(r, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            add(name!, GetString(r, "resourceType"), GetDecimal(r, "monthlyCost"));
        }
    }

    private static int GetUnsupportedCount(JsonElement root)
    {
        if (root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.Object
            && summary.TryGetProperty("totalUnsupportedResources", out var v) && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out var i))
            return i;
        return 0;
    }

    private static string? GetString(JsonElement obj, string name) =>
        obj.ValueKind == JsonValueKind.Object
        && obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>Infracost amounts are invariant-culture decimal strings (occasionally numbers). Null when absent/blank.</summary>
    private static decimal? GetDecimal(JsonElement obj, string name)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(name, out var v))
            return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out var num))
            return num;
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = v.GetString();
            if (!string.IsNullOrWhiteSpace(s) && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
        }
        return null;
    }
}
