namespace Fenrix.IaCStudio.Contracts.Terraform;

// ---- Provider schema model (terraform providers schema -json, parsed & cached offline) ----
// See docs/07-visual-builder.md. This is the machine-readable description of every installed provider's
// configuration, resources, and data sources: attribute names, types, required/optional/computed/sensitive
// flags, and nested blocks. It backs the schema-driven forms — it never carries live values, so it is safe
// to cache on disk under Cache/terraform-schemas.

/// <summary>The Terraform value-type family of a schema attribute.</summary>
public enum TfTypeKind
{
    String = 0,
    Number = 1,
    Bool = 2,
    List = 3,
    Set = 4,
    Map = 5,
    Object = 6,
    Tuple = 7,
    /// <summary>The <c>any</c> / dynamic pseudo-type — accepts any value; edited as raw HCL.</summary>
    Dynamic = 8
}

/// <summary>One member of an <see cref="TfTypeKind.Object"/> type: a named field with its own type.</summary>
public sealed record TfObjectField(string Name, TfType Type);

/// <summary>
/// A Terraform attribute type. Primitives (<c>string</c>/<c>number</c>/<c>bool</c>) carry only a
/// <see cref="Kind"/>; collections (<c>list</c>/<c>set</c>/<c>map</c>) carry an <see cref="Element"/>;
/// <c>object</c> carries <see cref="ObjectFields"/>; <c>tuple</c> carries <see cref="TupleElements"/>.
/// <see cref="Label"/> is the human-readable rendering (e.g. <c>map(string)</c>).
/// </summary>
public sealed record TfType(
    TfTypeKind Kind,
    string Label,
    TfType? Element = null,
    IReadOnlyList<TfObjectField>? ObjectFields = null,
    IReadOnlyList<TfType>? TupleElements = null)
{
    public bool IsPrimitive => Kind is TfTypeKind.String or TfTypeKind.Number or TfTypeKind.Bool;
    public bool IsCollection => Kind is TfTypeKind.List or TfTypeKind.Set or TfTypeKind.Map;

    public static readonly TfType String = new(TfTypeKind.String, "string");
    public static readonly TfType Number = new(TfTypeKind.Number, "number");
    public static readonly TfType Bool = new(TfTypeKind.Bool, "bool");
    public static readonly TfType Dynamic = new(TfTypeKind.Dynamic, "any");
}

/// <summary>
/// A single configurable/computed attribute of a schema block. The flags mirror the provider schema:
/// <see cref="Required"/> must be set, <see cref="Optional"/> may be set, <see cref="Computed"/> is
/// provider-populated (read-only unless also optional), and <see cref="Sensitive"/> marks secret values.
/// </summary>
public sealed record SchemaAttribute(
    string Name,
    TfType Type,
    bool Required,
    bool Optional,
    bool Computed,
    bool Sensitive,
    bool Deprecated,
    string? Description,
    SchemaNestedAttributeType? NestedType = null)
{
    /// <summary>True when the user may supply a value (required or optional). Pure-computed attrs are read-only.</summary>
    public bool IsConfigurable => Required || Optional;

    /// <summary>True when the attribute is provider-computed only and must not appear as an editable field.</summary>
    public bool IsReadOnly => Computed && !Required && !Optional;
}

/// <summary>
/// A newer-style structural nested attribute type (<c>nested_type</c> in the schema JSON): an inline object
/// whose <see cref="NestingMode"/> decides whether it is a single object, a list/set of objects, or a map.
/// </summary>
public sealed record SchemaNestedAttributeType(
    BlockNestingMode NestingMode,
    IReadOnlyList<SchemaAttribute> Attributes);

/// <summary>How a nested block repeats within its parent. Mirrors the provider schema <c>nesting_mode</c>.</summary>
public enum BlockNestingMode
{
    Single = 0,
    List = 1,
    Set = 2,
    Map = 3,
    /// <summary>The legacy <c>group</c> mode (rare); treated like a single block for authoring.</summary>
    Group = 4
}

/// <summary>
/// A nested configuration block (e.g. <c>ebs_block_device</c> inside <c>aws_instance</c>): its block name,
/// how it repeats, the inner block schema, and any cardinality constraints.
/// </summary>
public sealed record SchemaNestedBlock(
    string TypeName,
    BlockNestingMode NestingMode,
    SchemaBlock Block,
    int MinItems,
    int MaxItems);

/// <summary>
/// A schema block: its attributes and nested blocks, plus an optional description. Shared by provider config,
/// resource, and data-source schemas (all are "a block").
/// </summary>
public sealed record SchemaBlock(
    IReadOnlyList<SchemaAttribute> Attributes,
    IReadOnlyList<SchemaNestedBlock> NestedBlocks,
    string? Description)
{
    /// <summary>Configurable required attributes, name-sorted — shown first in forms.</summary>
    public IReadOnlyList<SchemaAttribute> RequiredAttributes =>
        Attributes.Where(a => a.Required).OrderBy(a => a.Name, StringComparer.Ordinal).ToList();

    /// <summary>Configurable optional (incl. optional+computed) attributes, name-sorted — shown collapsed.</summary>
    public IReadOnlyList<SchemaAttribute> OptionalAttributes =>
        Attributes.Where(a => a.Optional && !a.Required).OrderBy(a => a.Name, StringComparer.Ordinal).ToList();

    public static readonly SchemaBlock Empty = new([], [], null);
}

/// <summary>A named resource or data-source type with its schema version and block. E.g. <c>aws_instance</c>.</summary>
public sealed record ResourceTypeSchema(string Type, long? Version, SchemaBlock Block);

/// <summary>
/// One provider's full schema: its fully-qualified <see cref="Address"/> (e.g.
/// <c>registry.terraform.io/hashicorp/aws</c>), the derived source/local name for <c>required_providers</c>,
/// its provider-config block, and its resource + data-source type schemas.
/// </summary>
public sealed record ProviderSchema(
    string Address,
    SchemaBlock? ProviderConfig,
    IReadOnlyList<ResourceTypeSchema> ResourceSchemas,
    IReadOnlyList<ResourceTypeSchema> DataSourceSchemas)
{
    /// <summary>The registry source (<c>namespace/type</c>), dropping the default <c>registry.terraform.io</c> host.</summary>
    public string Source
    {
        get
        {
            var parts = Address.Split('/');
            // host/namespace/type → namespace/type; already namespace/type stays as-is.
            if (parts.Length >= 3)
            {
                var host = parts[0];
                var rest = string.Join('/', parts[^2], parts[^1]);
                return host.Equals("registry.terraform.io", StringComparison.OrdinalIgnoreCase) ? rest : $"{host}/{rest}";
            }
            return Address;
        }
    }

    /// <summary>The short local name used to prefix resources (e.g. <c>aws</c>) — the last address segment.</summary>
    public string LocalName
    {
        get
        {
            var parts = Address.Split('/');
            return parts.Length == 0 ? Address : parts[^1];
        }
    }

    public int ResourceCount => ResourceSchemas.Count;
    public int DataSourceCount => DataSourceSchemas.Count;
}

/// <summary>
/// The parsed, cached set of provider schemas. Produced from <c>terraform providers schema -json</c> and the
/// backbone of the visual builder. See docs/07-visual-builder.md.
/// </summary>
public sealed record ProviderSchemaSet(string? FormatVersion, IReadOnlyList<ProviderSchema> Providers)
{
    public bool IsEmpty => Providers.Count == 0;

    public static readonly ProviderSchemaSet Empty = new(null, []);

    /// <summary>Finds a provider by fully-qualified address (case-insensitive).</summary>
    public ProviderSchema? FindProvider(string address) =>
        Providers.FirstOrDefault(p => string.Equals(p.Address, address, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Metadata about the on-disk schema cache for a project: when it was captured, how many providers it holds,
/// and the provider-lock hash it was captured against (so the UI can flag it as stale after <c>init -upgrade</c>).
/// </summary>
public sealed record ProviderSchemaCacheInfo(
    bool Exists,
    DateTimeOffset? CapturedAt,
    int ProviderCount,
    string? LockHash)
{
    public static readonly ProviderSchemaCacheInfo Missing = new(false, null, 0, null);
}

/// <summary>
/// Outcome of refreshing the provider-schema cache by running <c>providers schema -json</c>: the parsed set,
/// a block reason when Fenrix refused to run (missing binary/dir/version), and whether the command succeeded
/// (it fails when the providers aren't installed yet — the UI then prompts to run <c>init</c>).
/// </summary>
public sealed record SchemaRefreshResult(ProviderSchemaSet Schema, string? BlockReason, bool Succeeded)
{
    public static SchemaRefreshResult Blocked(string reason) => new(ProviderSchemaSet.Empty, reason, false);
}
