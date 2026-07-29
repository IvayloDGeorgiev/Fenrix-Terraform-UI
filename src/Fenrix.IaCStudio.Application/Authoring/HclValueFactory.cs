using System.Globalization;
using Fenrix.IaCStudio.Application.Hcl;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Authoring;

/// <summary>
/// Converts a user-entered form field into an <see cref="HclValue"/> for a given schema attribute type. When
/// the user marks a field as an expression (or the type is a collection/object/dynamic), the text is kept as a
/// raw HCL expression so references, functions, and interpolations pass through untouched — the deliberate
/// limitation of docs/07-visual-builder.md. Primitive fields become plain literals.
/// </summary>
public static class HclValueFactory
{
    /// <summary>
    /// Builds a value from entered <paramref name="text"/>. <paramref name="asExpression"/> forces a raw
    /// expression; otherwise the value is a literal appropriate to <paramref name="type"/>.
    /// </summary>
    public static HclValue FromInput(TfType type, string text, bool asExpression)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (asExpression)
            return HclValue.Raw(text ?? string.Empty);

        return type.Kind switch
        {
            TfTypeKind.String => new HclString(text ?? string.Empty),
            TfTypeKind.Number => IsNumber(trimmed) ? new HclNumber(trimmed) : HclValue.Raw(trimmed),
            TfTypeKind.Bool => ParseBool(trimmed),
            // Collections / objects / tuples / dynamic are entered as raw HCL expressions.
            _ => HclValue.Raw(text ?? string.Empty)
        };
    }

    private static HclValue ParseBool(string text) => text.ToLowerInvariant() switch
    {
        "true" => new HclBool(true),
        "false" => new HclBool(false),
        _ => HclValue.Raw(text)
    };

    private static bool IsNumber(string text) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
}
