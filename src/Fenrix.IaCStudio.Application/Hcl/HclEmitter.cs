using System.Text;

namespace Fenrix.IaCStudio.Application.Hcl;

/// <summary>
/// Renders an <see cref="HclBlock"/> (or a standalone <see cref="HclValue"/>) to canonical, 2-space-indented
/// HCL text — close enough to <c>terraform fmt</c> that the file reads cleanly, and safe to run through
/// <c>fmt</c> afterwards for exact canonicalisation. Pure and deterministic, so the reference port can assert
/// on the exact output. See docs/07-visual-builder.md.
/// </summary>
public static class HclEmitter
{
    private const string Indent = "  ";

    /// <summary>Emits a top-level block (no trailing newline).</summary>
    public static string Emit(HclBlock block)
    {
        var sb = new StringBuilder();
        EmitBlock(sb, block, 0);
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Emits a single value (used for in-place literal edits / previews).</summary>
    public static string EmitValue(HclValue value) => RenderValue(value, 0);

    private static void EmitBlock(StringBuilder sb, HclBlock block, int depth)
    {
        var pad = Pad(depth);
        sb.Append(pad).Append(block.Type);
        foreach (var label in block.Labels)
            sb.Append(' ').Append('"').Append(EscapeString(label)).Append('"');
        sb.Append(" {\n");

        var inner = depth + 1;
        foreach (var arg in block.Arguments)
        {
            sb.Append(Pad(inner)).Append(arg.Name).Append(" = ").Append(RenderValue(arg.Value, inner)).Append('\n');
        }

        if (block.Arguments.Count > 0 && block.Blocks.Count > 0)
            sb.Append('\n');

        for (var i = 0; i < block.Blocks.Count; i++)
        {
            EmitBlock(sb, block.Blocks[i], inner);
            if (i < block.Blocks.Count - 1)
                sb.Append('\n');
        }

        sb.Append(pad).Append("}\n");
    }

    private static string RenderValue(HclValue value, int depth) => value switch
    {
        HclString s => $"\"{EscapeString(s.Value)}\"",
        HclNumber n => n.Text,
        HclBool b => b.Value ? "true" : "false",
        HclNull => "null",
        HclRaw r => r.Expression,
        HclList list => RenderList(list, depth),
        HclObject obj => RenderObject(obj, depth),
        _ => "null"
    };

    private static string RenderList(HclList list, int depth)
    {
        if (list.Items.Count == 0)
            return "[]";

        // Single-line when every item is a scalar/raw; multi-line when items are themselves lists/objects.
        var multiline = list.Items.Any(i => i is HclList or HclObject);
        if (!multiline)
            return "[" + string.Join(", ", list.Items.Select(i => RenderValue(i, depth))) + "]";

        var sb = new StringBuilder("[\n");
        var inner = depth + 1;
        for (var i = 0; i < list.Items.Count; i++)
        {
            sb.Append(Pad(inner)).Append(RenderValue(list.Items[i], inner));
            sb.Append(i < list.Items.Count - 1 ? ",\n" : "\n");
        }
        sb.Append(Pad(depth)).Append(']');
        return sb.ToString();
    }

    private static string RenderObject(HclObject obj, int depth)
    {
        if (obj.Entries.Count == 0)
            return "{}";

        var sb = new StringBuilder("{\n");
        var inner = depth + 1;
        foreach (var entry in obj.Entries)
            sb.Append(Pad(inner)).Append(FormatKey(entry.Key)).Append(" = ").Append(RenderValue(entry.Value, inner)).Append('\n');
        sb.Append(Pad(depth)).Append('}');
        return sb.ToString();
    }

    /// <summary>Object keys are bare when they are valid identifiers, otherwise quoted.</summary>
    private static string FormatKey(string key) => IsIdentifier(key) ? key : $"\"{EscapeString(key)}\"";

    public static bool IsIdentifier(string s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        if (!(char.IsLetter(s[0]) || s[0] == '_'))
            return false;
        foreach (var c in s)
        {
            if (!(char.IsLetterOrDigit(c) || c is '_' or '-'))
                return false;
        }
        return true;
    }

    internal static string EscapeString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        // ${ and %{ open interpolation / template directives; double the sigil so a literal stays literal.
        return sb.ToString().Replace("${", "$${").Replace("%{", "%%{");
    }

    private static string Pad(int depth) => depth == 0 ? string.Empty : string.Concat(Enumerable.Repeat(Indent, depth));
}
