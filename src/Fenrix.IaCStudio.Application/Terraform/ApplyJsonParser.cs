using System.Text.Json;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Parses individual lines of the <c>terraform apply -json</c> stream into structured events. Terraform
/// emits newline-delimited JSON objects tagged with a <c>type</c>; this turns the resource-hook lines into
/// <see cref="ApplyProgressEvent"/> (per-resource status transitions) and the <c>change_summary</c> line
/// into final counts. Only resource addresses/status/timing are surfaced — no attribute values — so the
/// structured view carries nothing sensitive. See docs/25-execution-lifecycle.md.
/// </summary>
public static class ApplyJsonParser
{
    /// <summary>Final apply totals from a <c>change_summary</c> line.</summary>
    public readonly record struct ApplyChangeCounts(int Add, int Change, int Remove, string Operation);

    /// <summary>
    /// Parses a single line as a resource-progress event. Returns null for blank lines, non-JSON lines,
    /// or JSON events that aren't per-resource apply hooks (diagnostics, summaries, outputs, versions).
    /// </summary>
    public static ApplyProgressEvent? TryParseProgress(string line)
    {
        var root = TryParseObject(line);
        if (root is null)
            return null;

        var el = root.Value;
        var type = GetString(el, "type");
        var status = type switch
        {
            "apply_start" => ApplyResourceStatus.InProgress,
            "apply_progress" => ApplyResourceStatus.InProgress,
            "apply_complete" => ApplyResourceStatus.Complete,
            "apply_errored" => ApplyResourceStatus.Errored,
            _ => (ApplyResourceStatus?)null
        };
        if (status is null)
            return null;

        if (!el.TryGetProperty("hook", out var hook) || hook.ValueKind != JsonValueKind.Object)
            return null;
        if (!hook.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.Object)
            return null;

        var address = GetString(resource, "addr") ?? string.Empty;
        var resourceType = GetString(resource, "resource_type") ?? string.Empty;
        var provider = GetString(resource, "implied_provider") ?? string.Empty;
        var action = MapAction(GetString(hook, "action"));
        var elapsed = GetDouble(hook, "elapsed_seconds");
        var message = GetString(el, "@message");
        var timestamp = GetTimestamp(el);

        return new ApplyProgressEvent(address, resourceType, provider, action, status.Value, elapsed, message, timestamp);
    }

    /// <summary>Parses a single line as a <c>change_summary</c>, returning the totals, or null otherwise.</summary>
    public static ApplyChangeCounts? TryParseChangeSummary(string line)
    {
        var root = TryParseObject(line);
        if (root is null)
            return null;

        var el = root.Value;
        if (GetString(el, "type") != "change_summary")
            return null;
        if (!el.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Object)
            return null;

        return new ApplyChangeCounts(
            GetInt(changes, "add"),
            GetInt(changes, "change"),
            GetInt(changes, "remove"),
            GetString(changes, "operation") ?? string.Empty);
    }

    private static ApplyResourceAction MapAction(string? action) => action switch
    {
        "create" => ApplyResourceAction.Create,
        "update" => ApplyResourceAction.Update,
        "delete" => ApplyResourceAction.Delete,
        "read" => ApplyResourceAction.Read,
        _ => ApplyResourceAction.Unknown
    };

    private static JsonElement? TryParseObject(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var trimmed = line.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return null;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            // Clone so the element remains valid after the document is disposed.
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset GetTimestamp(JsonElement el)
    {
        var raw = GetString(el, "@timestamp");
        return raw is not null && DateTimeOffset.TryParse(raw, out var ts) ? ts : DateTimeOffset.Now;
    }

    private static string? GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int GetInt(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static double? GetDouble(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;
}
