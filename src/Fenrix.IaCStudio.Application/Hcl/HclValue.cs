namespace Fenrix.IaCStudio.Application.Hcl;

/// <summary>
/// A minimal HCL value model used by the visual builder. It covers the value shapes the builder authors and
/// edits as literals — strings, numbers, bools, null, lists, and objects — plus an escape hatch
/// (<see cref="HclRaw"/>) for any expression the builder does not model graphically (references, functions,
/// interpolations, heredocs). Advanced HCL always round-trips as raw source, per docs/07-visual-builder.md.
/// </summary>
public abstract record HclValue
{
    /// <summary>True when this value is a plain literal (not a raw expression) and can be shown as a typed field.</summary>
    public abstract bool IsLiteral { get; }

    public static HclValue String(string value) => new HclString(value);
    public static HclValue Number(string raw) => new HclNumber(raw);
    public static HclValue Bool(bool value) => new HclBool(value);
    public static HclValue Null { get; } = new HclNull();
    public static HclValue Raw(string expression) => new HclRaw(expression);
    public static HclValue List(IReadOnlyList<HclValue> items) => new HclList(items);
    public static HclValue Object(IReadOnlyList<KeyValuePair<string, HclValue>> entries) => new HclObject(entries);
}

/// <summary>A quoted string literal. <see cref="Value"/> is the decoded (unescaped) text.</summary>
public sealed record HclString(string Value) : HclValue
{
    public override bool IsLiteral => true;
}

/// <summary>A numeric literal, kept as its raw token so precision/format is preserved exactly.</summary>
public sealed record HclNumber(string Text) : HclValue
{
    public override bool IsLiteral => true;
}

/// <summary>A boolean literal.</summary>
public sealed record HclBool(bool Value) : HclValue
{
    public override bool IsLiteral => true;
}

/// <summary>The <c>null</c> literal.</summary>
public sealed record HclNull : HclValue
{
    public override bool IsLiteral => true;
}

/// <summary>A list/tuple literal.</summary>
public sealed record HclList(IReadOnlyList<HclValue> Items) : HclValue
{
    public override bool IsLiteral => Items.All(i => i.IsLiteral);
}

/// <summary>An object/map literal (<c>{ key = value }</c>).</summary>
public sealed record HclObject(IReadOnlyList<KeyValuePair<string, HclValue>> Entries) : HclValue
{
    public override bool IsLiteral => Entries.All(e => e.Value.IsLiteral);
}

/// <summary>
/// A raw HCL expression emitted verbatim — the escape hatch for anything the builder does not model as a
/// literal (references like <c>var.x</c>, function calls, <c>${…}</c> interpolations, heredocs, operators).
/// </summary>
public sealed record HclRaw(string Expression) : HclValue
{
    public override bool IsLiteral => false;
}
