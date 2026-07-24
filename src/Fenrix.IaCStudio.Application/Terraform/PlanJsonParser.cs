using System.Text.Json;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Parses the output of <c>terraform show -json &lt;plan&gt;</c> into a redacted <see cref="PlanReview"/>.
/// Runs entirely in memory: sensitive attribute values (flagged by Terraform's
/// <c>before_sensitive</c>/<c>after_sensitive</c> maps) are reduced to a placeholder and values not yet
/// known (<c>after_unknown</c>) are marked, so nothing sensitive leaves this method. See
/// docs/06-plan-apply-safety.md and docs/11-secrets.md.
/// </summary>
public static class PlanJsonParser
{
    private const string SensitivePlaceholder = ArgumentRedactor.Placeholder;
    private const int MaxRenderedLength = 500;

    /// <summary>Parses show-json text; returns <see cref="PlanReview.Empty"/> for blank or unparseable input.</summary>
    public static PlanReview Parse(string showJson)
    {
        if (string.IsNullOrWhiteSpace(showJson))
            return PlanReview.Empty;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(showJson); }
        catch (JsonException) { return PlanReview.Empty; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return PlanReview.Empty;

            var formatVersion = GetString(root, "format_version");
            var tfVersion = GetString(root, "terraform_version");

            var changes = ParseResourceArray(root, "resource_changes");
            var drift = ParseResourceArray(root, "resource_drift");
            var outputs = ParseOutputChanges(root);
            var summary = Summarize(changes, drift.Count);

            return new PlanReview(formatVersion, tfVersion, changes, drift, outputs, summary);
        }
    }

    private static PlanChangeSummary Summarize(IReadOnlyList<PlanResourceChange> changes, int driftCount)
    {
        int add = 0, change = 0, destroy = 0, replace = 0, read = 0, noop = 0;
        foreach (var c in changes)
        {
            switch (c.Action)
            {
                case ChangeAction.Create: add++; break;
                case ChangeAction.Update: change++; break;
                case ChangeAction.Delete: destroy++; break;
                case ChangeAction.Replace: replace++; break;
                case ChangeAction.Read: read++; break;
                default: noop++; break;
            }
        }
        return new PlanChangeSummary(add, change, destroy, replace, read, noop, driftCount);
    }

    private static List<PlanResourceChange> ParseResourceArray(JsonElement root, string property)
    {
        var list = new List<PlanResourceChange>();
        if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var rc in arr.EnumerateArray())
        {
            if (rc.ValueKind != JsonValueKind.Object)
                continue;

            var address = GetString(rc, "address") ?? string.Empty;
            var moduleAddress = GetString(rc, "module_address");
            var mode = GetString(rc, "mode") == "data" ? ResourceMode.Data : ResourceMode.Managed;
            var type = GetString(rc, "type") ?? string.Empty;
            var name = GetString(rc, "name") ?? string.Empty;
            var provider = ShortenProvider(GetString(rc, "provider_name"));
            var actionReason = GetString(rc, "action_reason");
            if (string.Equals(actionReason, "none", StringComparison.OrdinalIgnoreCase))
                actionReason = null;

            var action = ChangeAction.NoOp;
            IReadOnlyList<string> replacePaths = [];
            IReadOnlyList<AttributeChange> attributes = [];

            if (rc.TryGetProperty("change", out var change) && change.ValueKind == JsonValueKind.Object)
            {
                action = ParseActions(change);
                replacePaths = ParseReplacePaths(change);
                attributes = ParseAttributes(change);
            }

            list.Add(new PlanResourceChange(
                address, moduleAddress, mode, type, name, provider, action, actionReason, replacePaths, attributes));
        }
        return list;
    }

    private static ChangeAction ParseActions(JsonElement change)
    {
        if (!change.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
            return ChangeAction.NoOp;

        var values = new List<string>();
        foreach (var a in actions.EnumerateArray())
            if (a.ValueKind == JsonValueKind.String)
                values.Add(a.GetString()!);

        if (values.Count == 0)
            return ChangeAction.NoOp;

        // A replace is expressed as delete+create (order varies: create-before-destroy vs destroy-before-create).
        if (values.Contains("create") && values.Contains("delete"))
            return ChangeAction.Replace;

        return values[0] switch
        {
            "create" => ChangeAction.Create,
            "read" => ChangeAction.Read,
            "update" => ChangeAction.Update,
            "delete" => ChangeAction.Delete,
            "forget" => ChangeAction.Forget,
            _ => ChangeAction.NoOp
        };
    }

    private static IReadOnlyList<string> ParseReplacePaths(JsonElement change)
    {
        if (!change.TryGetProperty("replace_paths", out var rp) || rp.ValueKind != JsonValueKind.Array)
            return [];

        var paths = new List<string>();
        foreach (var path in rp.EnumerateArray())
        {
            if (path.ValueKind != JsonValueKind.Array)
                continue;
            var segments = path.EnumerateArray()
                .Select(seg => seg.ValueKind == JsonValueKind.String ? seg.GetString() : seg.GetRawText());
            paths.Add(string.Join('.', segments));
        }
        return paths;
    }

    private static IReadOnlyList<AttributeChange> ParseAttributes(JsonElement change)
    {
        var before = TryProp(change, "before");
        var after = TryProp(change, "after");
        var afterUnknown = TryProp(change, "after_unknown");
        var beforeSensitive = TryProp(change, "before_sensitive");
        var afterSensitive = TryProp(change, "after_sensitive");

        // Union of top-level keys present in before/after (both may be null for reads/no-ops).
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        CollectKeys(before, keys);
        CollectKeys(after, keys);
        if (keys.Count == 0)
            return [];

        var result = new List<AttributeChange>(keys.Count);
        foreach (var key in keys)
        {
            var beforeEl = TryProp(before, key);
            var afterEl = TryProp(after, key);
            var unknown = AnyTrue(TryProp(afterUnknown, key));
            var sensitive = AnyTrue(TryProp(beforeSensitive, key)) || AnyTrue(TryProp(afterSensitive, key));

            string? beforeStr = beforeEl is null ? null : Render(beforeEl.Value);
            string? afterStr = unknown ? null : (afterEl is null ? null : Render(afterEl.Value));

            if (sensitive)
            {
                if (beforeStr is not null) beforeStr = SensitivePlaceholder;
                if (afterStr is not null) afterStr = SensitivePlaceholder;
            }

            result.Add(new AttributeChange(key, beforeStr, afterStr, sensitive, unknown));
        }
        return result;
    }

    private static IReadOnlyList<PlanOutputChange> ParseOutputChanges(JsonElement root)
    {
        if (!root.TryGetProperty("output_changes", out var oc) || oc.ValueKind != JsonValueKind.Object)
            return [];

        var result = new List<PlanOutputChange>();
        foreach (var prop in oc.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
                continue;
            var action = ParseActions(prop.Value);
            var sensitive = AnyTrue(TryProp(prop.Value, "before_sensitive")) || AnyTrue(TryProp(prop.Value, "after_sensitive"));
            var unknown = AnyTrue(TryProp(prop.Value, "after_unknown"));
            result.Add(new PlanOutputChange(prop.Name, action, sensitive, unknown));
        }
        return result;
    }

    // ---- JSON helpers ----

    private static void CollectKeys(JsonElement? el, SortedSet<string> keys)
    {
        if (el is { ValueKind: JsonValueKind.Object } o)
            foreach (var p in o.EnumerateObject())
                keys.Add(p.Name);
    }

    /// <summary>Returns the named property as a nullable element, or null if the container isn't an object or lacks it.</summary>
    private static JsonElement? TryProp(JsonElement? container, string name)
    {
        if (container is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty(name, out var v))
            return v;
        return null;
    }

    /// <summary>True if the element is JSON <c>true</c>, or a nested array/object containing any <c>true</c>.</summary>
    private static bool AnyTrue(JsonElement? element)
    {
        if (element is null)
            return false;

        var e = element.Value;
        switch (e.ValueKind)
        {
            case JsonValueKind.True:
                return true;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                    if (AnyTrue(item)) return true;
                return false;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    if (AnyTrue(p.Value)) return true;
                return false;
            default:
                return false;
        }
    }

    private static string Render(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonValueKind.Number => el.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => Truncate(el.GetRawText())
    };

    private static string Truncate(string s) =>
        s.Length <= MaxRenderedLength ? s : string.Concat(s.AsSpan(0, MaxRenderedLength), "…");

    private static string ShortenProvider(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            return string.Empty;
        var slash = providerName.LastIndexOf('/');
        return slash >= 0 && slash < providerName.Length - 1 ? providerName[(slash + 1)..] : providerName;
    }

    private static string? GetString(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}
