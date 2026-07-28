using System.Text.Json;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Parses <c>terraform output -json</c> into a redacted <see cref="OutputCollection"/>. Each output object is
/// <c>{ "sensitive": bool, "type": …, "value": … }</c>; a sensitive output's value is reduced to a
/// placeholder so nothing sensitive leaves this method. See docs/06-plan-apply-safety.md, docs/11-secrets.md.
/// </summary>
public static class OutputJsonParser
{
    private const string SensitivePlaceholder = ArgumentRedactor.Placeholder;
    private const int MaxRenderedLength = 500;

    /// <summary>Parses output-json text; returns <see cref="OutputCollection.Empty"/> for blank/unparseable input.</summary>
    public static OutputCollection Parse(string outputJson)
    {
        if (string.IsNullOrWhiteSpace(outputJson))
            return OutputCollection.Empty;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(outputJson); }
        catch (JsonException) { return OutputCollection.Empty; }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return OutputCollection.Empty;

            var outputs = new List<TerraformOutput>();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                var v = prop.Value;
                var sensitive = v.TryGetProperty("sensitive", out var se) && se.ValueKind == JsonValueKind.True;
                var typeLabel = v.TryGetProperty("type", out var t) ? TypeLabel(t) : "unknown";
                string? value = sensitive
                    ? SensitivePlaceholder
                    : (v.TryGetProperty("value", out var val) ? Render(val) : null);

                outputs.Add(new TerraformOutput(prop.Name, typeLabel, value, sensitive));
            }
            outputs.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return new OutputCollection(outputs);
        }
    }

    /// <summary>
    /// Renders Terraform's type constraint to a short label. A simple type is a string (<c>"string"</c>);
    /// a complex type is an array whose first element names the kind (<c>["object", {…}]</c>, <c>["list","string"]</c>).
    /// </summary>
    private static string TypeLabel(JsonElement type) => type.ValueKind switch
    {
        JsonValueKind.String => type.GetString() ?? "unknown",
        JsonValueKind.Array when type.GetArrayLength() > 0 => type[0].ValueKind == JsonValueKind.String
            ? type[0].GetString() ?? "complex"
            : "complex",
        _ => "complex"
    };

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
}
