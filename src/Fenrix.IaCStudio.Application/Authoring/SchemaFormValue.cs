using Fenrix.IaCStudio.Application.Hcl;

namespace Fenrix.IaCStudio.Application.Authoring;

/// <summary>
/// The current output of a schema-driven form: the arguments and nested blocks the user has filled in. The
/// builder assembles these into a resource/data block for the live HCL preview. See docs/07-visual-builder.md.
/// </summary>
public sealed record SchemaFormValue(
    IReadOnlyList<HclArgument> Arguments,
    IReadOnlyList<HclBlock> Blocks)
{
    public static readonly SchemaFormValue Empty = new([], []);
}
