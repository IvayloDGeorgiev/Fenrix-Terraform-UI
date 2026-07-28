using System.Text.Json;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Parses the output of <c>terraform show -json</c> (no plan file → the current state) into a redacted
/// <see cref="StateSnapshot"/>. Runs entirely in memory: attribute values flagged by the state JSON
/// <c>sensitive_values</c> map are reduced to a placeholder, so nothing sensitive leaves this method.
/// Recurses <c>values.root_module.child_modules</c> so nested-module resources are included. When the input
/// is a raw state document (from <c>state pull</c>) with top-level <c>serial</c>/<c>lineage</c>, those are
/// captured too. See docs/06-plan-apply-safety.md, docs/11-secrets.md, docs/22-terraform-files-model.md.
/// </summary>
public static class StateJsonParser
{
    private const string SensitivePlaceholder = ArgumentRedactor.Placeholder;
    private const int MaxRenderedLength = 500;

    /// <summary>Parses show-json state text; returns <see cref="StateSnapshot.Empty"/> for blank/unparseable input.</summary>
    public static StateSnapshot Parse(string showJson)
    {
        if (string.IsNullOrWhiteSpace(showJson))
            return StateSnapshot.Empty;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(showJson); }
        catch (JsonException) { return StateSnapshot.Empty; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return StateSnapshot.Empty;

            var formatVersion = GetString(root, "format_version");
            var tfVersion = GetString(root, "terraform_version");
            long? serial = root.TryGetProperty("serial", out var s) && s.TryGetInt64(out var sv) ? sv : null;
            var lineage = GetString(root, "lineage");

            var resources = new List<StateResourceInstance>();
            if (root.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Object &&
                values.TryGetProperty("root_module", out var rootModule) && rootModule.ValueKind == JsonValueKind.Object)
            {
                CollectModule(rootModule, resources);
            }

            return new StateSnapshot(formatVersion, tfVersion, serial, lineage, resources);
        }
    }

    private static void CollectModule(JsonElement module, List<StateResourceInstance> sink)
    {
        if (module.TryGetProperty("resources", out var arr) && arr.ValueKind == JsonValueKind.Array)
            foreach (var rc in arr.EnumerateArray())
                if (rc.ValueKind == JsonValueKind.Object)
                    sink.Add(ParseResource(rc));

        if (module.TryGetProperty("child_modules", out var kids) && kids.ValueKind == JsonValueKind.Array)
            foreach (var kid in kids.EnumerateArray())
                if (kid.ValueKind == JsonValueKind.Object)
                    CollectModule(kid, sink);
    }

    private static StateResourceInstance ParseResource(JsonElement rc)
    {
        var address = GetString(rc, "address") ?? string.Empty;
        var moduleAddress = ModuleAddressFromResource(address);
        var mode = GetString(rc, "mode") == "data" ? ResourceMode.Data : ResourceMode.Managed;
        var type = GetString(rc, "type") ?? string.Empty;
        var name = GetString(rc, "name") ?? string.Empty;
        var provider = ShortenProvider(GetString(rc, "provider_name"));

        int? indexOrdinal = null;
        if (rc.TryGetProperty("index", out var idx))
        {
            if (idx.ValueKind == JsonValueKind.Number && idx.TryGetInt32(out var n))
                indexOrdinal = n;
        }

        var attributes = ParseAttributes(rc);
        return new StateResourceInstance(address, moduleAddress, mode, type, name, provider, indexOrdinal, attributes);
    }

    private static IReadOnlyList<StateAttribute> ParseAttributes(JsonElement rc)
    {
        if (!rc.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
            return [];

        var sensitive = rc.TryGetProperty("sensitive_values", out var sv) && sv.ValueKind == JsonValueKind.Object
            ? sv
            : (JsonElement?)null;

        var result = new List<StateAttribute>();
        foreach (var prop in values.EnumerateObject())
        {
            var isSensitive = AnyTrue(TryProp(sensitive, prop.Name));
            var rendered = isSensitive ? SensitivePlaceholder : Render(prop.Value);
            result.Add(new StateAttribute(prop.Name, rendered, isSensitive));
        }
        result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return result;
    }

    /// <summary>Derives the module portion of an address (e.g. <c>module.x.aws_s3_bucket.b</c> → <c>module.x</c>).</summary>
    private static string? ModuleAddressFromResource(string address)
    {
        if (!address.StartsWith("module.", StringComparison.Ordinal))
            return null;
        // The module path is every "module.<name>" pair before the resource segment.
        var segments = address.Split('.');
        var parts = new List<string>();
        for (var i = 0; i + 1 < segments.Length; i += 2)
        {
            if (segments[i] != "module")
                break;
            parts.Add($"module.{segments[i + 1]}");
        }
        return parts.Count > 0 ? string.Join('.', parts) : null;
    }

    // ---- JSON helpers (shared shape with PlanJsonParser) ----

    private static JsonElement? TryProp(JsonElement? container, string name)
    {
        if (container is { ValueKind: JsonValueKind.Object } c && c.TryGetProperty(name, out var v))
            return v;
        return null;
    }

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
