namespace Fenrix.IaCStudio.Contracts.Terraform;

/// <summary>Severity of a validation diagnostic, mirroring Terraform's <c>diagnostics[].severity</c>.</summary>
public enum DiagnosticSeverity
{
    Error = 0,
    Warning = 1
}

/// <summary>One diagnostic from <c>terraform validate -json</c>. See docs/05-terraform-engine.md.</summary>
public sealed record ValidationDiagnostic(
    DiagnosticSeverity Severity,
    string Summary,
    string? Detail,
    string? FileName,
    int? Line);

/// <summary>
/// Parsed result of <c>terraform validate -json</c> (<c>format_version</c> "1.0"). When JSON could not
/// be parsed, <see cref="ParsedFromJson"/> is false and callers fall back to the raw text output.
/// See docs/05-terraform-engine.md.
/// </summary>
public sealed record TerraformValidationResult(
    bool Valid,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<ValidationDiagnostic> Diagnostics,
    bool ParsedFromJson)
{
    public static TerraformValidationResult Unparsed(bool valid) =>
        new(valid, valid ? 0 : 1, 0, Array.Empty<ValidationDiagnostic>(), ParsedFromJson: false);
}
