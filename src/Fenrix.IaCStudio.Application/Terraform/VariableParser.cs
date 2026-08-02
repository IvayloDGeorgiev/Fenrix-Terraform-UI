using Fenrix.IaCStudio.Application.Hcl;
using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Pure parsing for the variables manager (Phase 12): reads <c>variable "x" { … }</c> declarations from
/// configuration and <c>name = value</c> assignments from a tfvars file, using the shared HCL toolkit. Side-effect
/// free and fixture-testable; the Infrastructure service supplies the file contents. See docs/33-variables.md.
/// </summary>
public static class VariableParser
{
    /// <summary>A parsed variable declaration (before the tfvars value is merged in).</summary>
    public sealed record Declaration(
        string Name, string TypeExpression, VariableKind Kind, string? Description,
        bool Sensitive, bool HasDefault, string? DefaultRaw);

    /// <summary>Parses all <c>variable</c> blocks from one .tf file's content.</summary>
    public static IReadOnlyList<Declaration> ParseDeclarations(string tfContent)
    {
        var result = new List<Declaration>();
        if (string.IsNullOrWhiteSpace(tfContent)) return result;

        foreach (var block in HclReader.ReadOutline(tfContent))
        {
            if (!string.Equals(block.Type, "variable", StringComparison.Ordinal) || block.Labels.Count == 0)
                continue;

            var name = block.Labels[0];
            var args = HclReader.ReadArguments(tfContent, block);

            string typeExpr = "any";
            string? description = null;
            var sensitive = false;
            var hasDefault = false;
            string? defaultRaw = null;

            foreach (var a in args)
            {
                switch (a.Name)
                {
                    case "type":
                        typeExpr = a.RawValueText.Trim();
                        break;
                    case "description":
                        description = a.Value is HclString s ? s.Value : Unquote(a.RawValueText);
                        break;
                    case "sensitive":
                        sensitive = a.RawValueText.Trim() == "true";
                        break;
                    case "default":
                        hasDefault = true;
                        defaultRaw = a.RawValueText.Trim();
                        break;
                }
            }

            result.Add(new Declaration(name, typeExpr, KindOf(typeExpr), description, sensitive, hasDefault, defaultRaw));
        }

        return result;
    }

    /// <summary>Parses <c>name = value</c> assignments from a tfvars file into name → raw-value.</summary>
    public static IReadOnlyDictionary<string, string> ParseTfvars(string tfvarsContent)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(tfvarsContent)) return values;

        // tfvars is top-level `name = value`; wrap it in a synthetic block so the block-oriented reader can
        // parse the assignments. Offsets don't matter here — only names and raw value text.
        var wrapped = "fenrixwrap {\n" + tfvarsContent + "\n}";
        var outline = HclReader.ReadOutline(wrapped);
        if (outline.Count == 0) return values;

        foreach (var a in HclReader.ReadArguments(wrapped, outline[0]))
            values[a.Name] = a.RawValueText.Trim();

        return values;
    }

    /// <summary>Merges declarations (across files) with tfvars values into the editable view model.</summary>
    public static IReadOnlyList<ManagedVariable> Merge(
        IEnumerable<Declaration> declarations, IReadOnlyDictionary<string, string> tfvars)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var list = new List<ManagedVariable>();
        foreach (var d in declarations)
        {
            if (!seen.Add(d.Name)) continue; // first declaration wins
            var value = tfvars.TryGetValue(d.Name, out var raw) ? raw : null;
            list.Add(new ManagedVariable(
                d.Name, d.TypeExpression, d.Kind, d.Description, d.Sensitive, d.HasDefault, d.DefaultRaw, value));
        }
        return list.OrderBy(v => v.IsMissing ? 0 : 1).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static VariableKind KindOf(string typeExpr)
    {
        var t = typeExpr.Trim();
        if (t.StartsWith("string", StringComparison.OrdinalIgnoreCase)) return VariableKind.String;
        if (t.StartsWith("number", StringComparison.OrdinalIgnoreCase)) return VariableKind.Number;
        if (t.StartsWith("bool", StringComparison.OrdinalIgnoreCase)) return VariableKind.Bool;
        return VariableKind.Complex;
    }

    private static string Unquote(string raw)
    {
        var t = raw.Trim();
        return t.Length >= 2 && t[0] == '"' && t[^1] == '"' ? t[1..^1] : t;
    }
}
