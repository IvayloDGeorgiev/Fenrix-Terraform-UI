using Fenrix.IaCStudio.Application.Hcl;

namespace Fenrix.IaCStudio.Application.Authoring;

/// <summary>Outcome of writing generated HCL to a file (create/append or in-place literal edit).</summary>
public sealed record AuthoringWriteResult(bool Success, string RelativePath, string? Error)
{
    public static AuthoringWriteResult Ok(string path) => new(true, path, null);
    public static AuthoringWriteResult Fail(string path, string error) => new(false, path, error);
}

/// <summary>
/// An in-place literal edit: replace the exact source span <c>[ValueStart, ValueEnd)</c> with
/// <see cref="NewValueText"/>. Everything outside the span (comments, formatting, complex expressions, nested
/// blocks) is preserved byte-for-byte. Offsets come from <see cref="HclParsedArgument"/>.
/// </summary>
public sealed record LiteralEdit(int ValueStart, int ValueEnd, string NewValueText);

/// <summary>A config file loaded for round-trip editing: its content and located top-level blocks.</summary>
public sealed record AuthoringFile(string RelativePath, string Content, IReadOnlyList<HclBlockHandle> Blocks);
