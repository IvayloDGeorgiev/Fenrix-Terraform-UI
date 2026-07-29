namespace Fenrix.IaCStudio.Application.Hcl;

/// <summary>A single <c>name = value</c> argument within a block or object.</summary>
public sealed record HclArgument(string Name, HclValue Value);

/// <summary>
/// An HCL block model: a <see cref="Type"/> keyword (e.g. <c>resource</c>, <c>variable</c>, <c>provider</c>,
/// <c>terraform</c>, <c>module</c>, <c>data</c>, <c>output</c>, <c>locals</c>), optional string
/// <see cref="Labels"/>, ordered <see cref="Arguments"/>, and nested <see cref="Blocks"/>. Rendered to
/// canonical HCL by <see cref="HclEmitter"/>. See docs/07-visual-builder.md, docs/22-terraform-files-model.md.
/// </summary>
public sealed record HclBlock(
    string Type,
    IReadOnlyList<string> Labels,
    IReadOnlyList<HclArgument> Arguments,
    IReadOnlyList<HclBlock> Blocks)
{
    public HclBlock(string type, IReadOnlyList<string> labels)
        : this(type, labels, [], []) { }

    /// <summary>Returns a copy with an argument appended.</summary>
    public HclBlock WithArgument(string name, HclValue value) =>
        this with { Arguments = [.. Arguments, new HclArgument(name, value)] };

    /// <summary>Returns a copy with a nested block appended.</summary>
    public HclBlock WithBlock(HclBlock block) =>
        this with { Blocks = [.. Blocks, block] };
}
