using System.Text;
using System.Text.Json;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Parses the JSON emitted by <c>terraform providers schema -json</c> into the strongly-typed
/// <see cref="ProviderSchemaSet"/> that backs the visual builder. Pure and deterministic (no I/O), so it is
/// covered directly by the reference port. Malformed input degrades to <see cref="ProviderSchemaSet.Empty"/>
/// rather than throwing. See docs/07-visual-builder.md.
/// </summary>
public static class ProviderSchemaJsonParser
{
    public static ProviderSchemaSet Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return ProviderSchemaSet.Empty;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ProviderSchemaSet.Empty;

            var formatVersion = root.TryGetProperty("format_version", out var fv) ? fv.GetString() : null;

            var providers = new List<ProviderSchema>();
            if (root.TryGetProperty("provider_schemas", out var schemas) && schemas.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in schemas.EnumerateObject())
                    providers.Add(ParseProvider(prop.Name, prop.Value));
            }

            providers.Sort((a, b) => string.CompareOrdinal(a.Address, b.Address));
            return new ProviderSchemaSet(formatVersion, providers);
        }
        catch (JsonException)
        {
            return ProviderSchemaSet.Empty;
        }
    }

    private static ProviderSchema ParseProvider(string address, JsonElement element)
    {
        SchemaBlock? providerConfig = null;
        if (element.TryGetProperty("provider", out var provider) &&
            provider.TryGetProperty("block", out var providerBlock))
        {
            providerConfig = ParseBlock(providerBlock);
        }

        var resources = ParseTypeSchemas(element, "resource_schemas");
        var dataSources = ParseTypeSchemas(element, "data_source_schemas");

        return new ProviderSchema(address, providerConfig, resources, dataSources);
    }

    private static IReadOnlyList<ResourceTypeSchema> ParseTypeSchemas(JsonElement provider, string propertyName)
    {
        var list = new List<ResourceTypeSchema>();
        if (provider.TryGetProperty(propertyName, out var schemas) && schemas.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in schemas.EnumerateObject())
            {
                long? version = prop.Value.TryGetProperty("version", out var v) && v.TryGetInt64(out var n) ? n : null;
                var block = prop.Value.TryGetProperty("block", out var b) ? ParseBlock(b) : SchemaBlock.Empty;
                list.Add(new ResourceTypeSchema(prop.Name, version, block));
            }
        }
        list.Sort((a, b) => string.CompareOrdinal(a.Type, b.Type));
        return list;
    }

    private static SchemaBlock ParseBlock(JsonElement block)
    {
        var attributes = new List<SchemaAttribute>();
        if (block.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in attrs.EnumerateObject())
                attributes.Add(ParseAttribute(prop.Name, prop.Value));
        }

        var nestedBlocks = new List<SchemaNestedBlock>();
        if (block.TryGetProperty("block_types", out var blockTypes) && blockTypes.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in blockTypes.EnumerateObject())
                nestedBlocks.Add(ParseNestedBlock(prop.Name, prop.Value));
        }

        var description = block.TryGetProperty("description", out var d) ? d.GetString() : null;
        return new SchemaBlock(attributes, nestedBlocks, description);
    }

    private static SchemaAttribute ParseAttribute(string name, JsonElement attr)
    {
        var required = GetBool(attr, "required");
        var optional = GetBool(attr, "optional");
        var computed = GetBool(attr, "computed");
        var sensitive = GetBool(attr, "sensitive");
        var deprecated = GetBool(attr, "deprecated");
        var description = attr.TryGetProperty("description", out var d) ? d.GetString() : null;

        SchemaNestedAttributeType? nestedType = null;
        TfType type;
        if (attr.TryGetProperty("nested_type", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            var mode = ParseNestingMode(nested);
            var nestedAttrs = new List<SchemaAttribute>();
            if (nested.TryGetProperty("attributes", out var na) && na.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in na.EnumerateObject())
                    nestedAttrs.Add(ParseAttribute(prop.Name, prop.Value));
            }
            nestedType = new SchemaNestedAttributeType(mode, nestedAttrs);
            type = new TfType(TfTypeKind.Object, NestingLabel(mode));
        }
        else if (attr.TryGetProperty("type", out var t))
        {
            type = ParseType(t);
        }
        else
        {
            type = TfType.Dynamic;
        }

        return new SchemaAttribute(name, type, required, optional, computed, sensitive, deprecated, description, nestedType);
    }

    private static SchemaNestedBlock ParseNestedBlock(string typeName, JsonElement element)
    {
        var mode = ParseNestingMode(element);
        var block = element.TryGetProperty("block", out var b) ? ParseBlock(b) : SchemaBlock.Empty;
        var min = element.TryGetProperty("min_items", out var mi) && mi.TryGetInt32(out var minVal) ? minVal : 0;
        var max = element.TryGetProperty("max_items", out var ma) && ma.TryGetInt32(out var maxVal) ? maxVal : 0;
        return new SchemaNestedBlock(typeName, mode, block, min, max);
    }

    // ---- Terraform type expressions ----

    /// <summary>
    /// A type is either a JSON string primitive (<c>"string"</c>) or an array whose first element names a
    /// constructor: <c>["list", elem]</c>, <c>["map", elem]</c>, <c>["object", {field: type}]</c>,
    /// <c>["tuple", [type, …]]</c>.
    /// </summary>
    internal static TfType ParseType(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString() switch
            {
                "string" => TfType.String,
                "number" => TfType.Number,
                "bool" => TfType.Bool,
                _ => TfType.Dynamic
            };
        }

        if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() >= 1)
        {
            var ctor = element[0].GetString();
            switch (ctor)
            {
                case "list" or "set" or "map":
                {
                    var elem = element.GetArrayLength() >= 2 ? ParseType(element[1]) : TfType.Dynamic;
                    var kind = ctor switch { "list" => TfTypeKind.List, "set" => TfTypeKind.Set, _ => TfTypeKind.Map };
                    return new TfType(kind, $"{ctor}({elem.Label})", Element: elem);
                }
                case "object":
                {
                    var fields = new List<TfObjectField>();
                    if (element.GetArrayLength() >= 2 && element[1].ValueKind == JsonValueKind.Object)
                    {
                        foreach (var prop in element[1].EnumerateObject())
                            fields.Add(new TfObjectField(prop.Name, ParseType(prop.Value)));
                    }
                    var body = string.Join(", ", fields.Select(f => $"{f.Name} = {f.Type.Label}"));
                    return new TfType(TfTypeKind.Object, $"object({{{body}}})", ObjectFields: fields);
                }
                case "tuple":
                {
                    var elems = new List<TfType>();
                    if (element.GetArrayLength() >= 2 && element[1].ValueKind == JsonValueKind.Array)
                    {
                        foreach (var e in element[1].EnumerateArray())
                            elems.Add(ParseType(e));
                    }
                    var body = string.Join(", ", elems.Select(e => e.Label));
                    return new TfType(TfTypeKind.Tuple, $"tuple([{body}])", TupleElements: elems);
                }
            }
        }

        return TfType.Dynamic;
    }

    private static BlockNestingMode ParseNestingMode(JsonElement element) =>
        (element.TryGetProperty("nesting_mode", out var nm) ? nm.GetString() : null) switch
        {
            "single" => BlockNestingMode.Single,
            "list" => BlockNestingMode.List,
            "set" => BlockNestingMode.Set,
            "map" => BlockNestingMode.Map,
            "group" => BlockNestingMode.Group,
            _ => BlockNestingMode.Single
        };

    private static string NestingLabel(BlockNestingMode mode) => mode switch
    {
        BlockNestingMode.List => "list(object)",
        BlockNestingMode.Set => "set(object)",
        BlockNestingMode.Map => "map(object)",
        _ => "object"
    };

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
