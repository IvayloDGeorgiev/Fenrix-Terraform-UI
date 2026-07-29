using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Editor;

/// <summary>
/// The redacted preview of the editor's "Beautify" command (<c>terraform fmt -</c>) plus any reason Fenrix
/// would refuse to run it (missing binary, version-constraint violation, no environment). The buffer piped to
/// stdin is deliberately absent from the preview. See docs/05-terraform-engine.md, docs/23-command-transparency.md.
/// </summary>
public sealed record EditorFormatPreview(CommandPreview? Preview, string? BlockReason)
{
    public bool CanRun => BlockReason is null && Preview is not null;
}

/// <summary>
/// Outcome of formatting an editor buffer through <c>terraform fmt -</c> (stdin → stdout). On success,
/// <see cref="FormattedText"/> is the canonical HCL to swap into the buffer and <see cref="Changed"/> says
/// whether it differs from the input. <see cref="BlockReason"/> is set when Fenrix refused to run;
/// <see cref="Error"/> carries Terraform's own failure message (e.g. a syntax error in the buffer).
/// </summary>
public sealed record EditorFormatResult(
    bool Succeeded,
    string? FormattedText,
    bool Changed,
    string? BlockReason,
    string? Error)
{
    public static EditorFormatResult Blocked(string reason) => new(false, null, false, reason, null);
    public static EditorFormatResult Failed(string error) => new(false, null, false, null, error);
}

/// <summary>A scaffolded HCL snippet the editor can insert: its display name, category, and canonical body.</summary>
public sealed record EditorSnippet(string Key, string Title, string Category, string Description, string Body);

/// <summary>The kind of top-level construct an <see cref="OutlineSymbol"/> represents (drives its icon/label).</summary>
public enum OutlineSymbolKind
{
    Resource = 0,
    DataSource = 1,
    Variable = 2,
    Output = 3,
    Local = 4,
    Module = 5,
    Provider = 6,
    Terraform = 7,
    Backend = 8,
    Moved = 9,
    Import = 10,
    Other = 11
}

/// <summary>
/// One entry in the current file's outline: a top-level block (or a single <c>locals</c> entry), its display
/// label, and the 1-based line to jump to. Produced by <see cref="EditorOutlineBuilder"/> from the live buffer.
/// </summary>
public sealed record OutlineSymbol(OutlineSymbolKind Kind, string Label, string? Detail, int Line);

/// <summary>The category of an insertable reference in the <see cref="ReferenceIndex"/>.</summary>
public enum ReferenceKind
{
    Variable = 0,
    Local = 1,
    Output = 2,
    Module = 3,
    DataSource = 4,
    Resource = 5
}

/// <summary>
/// One insertable HCL reference (e.g. <c>var.region</c>, <c>module.network.vpc_id</c>,
/// <c>data.aws_ami.ubuntu.id</c>). <see cref="Detail"/> is an optional hint (declaring file, type, or attribute
/// source). <see cref="Attributes"/> lists schema attributes for resource/data references when the provider
/// schema cache is present, so the user can pick a specific attribute to append.
/// </summary>
public sealed record ReferenceEntry(
    ReferenceKind Kind,
    string Insert,
    string Label,
    string? Detail,
    IReadOnlyList<string> Attributes);

/// <summary>The gathered, categorised set of references available to the reference-helper palette.</summary>
public sealed record ReferenceIndex(IReadOnlyList<ReferenceEntry> Entries)
{
    public static readonly ReferenceIndex Empty = new([]);
}
