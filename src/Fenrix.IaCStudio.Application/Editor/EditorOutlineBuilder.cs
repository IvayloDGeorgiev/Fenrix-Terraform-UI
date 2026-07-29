using Fenrix.IaCStudio.Application.Hcl;

namespace Fenrix.IaCStudio.Application.Editor;

/// <summary>
/// Builds the "go-to-symbol" outline for the current editor buffer: every top-level block (and each individual
/// <c>locals</c> entry) with the 1-based line to jump to. Pure — reuses <see cref="HclReader.ReadOutline"/> and
/// tolerates malformed input (returns whatever it could locate). See docs/13-ui-design.md.
/// </summary>
public static class EditorOutlineBuilder
{
    public static IReadOnlyList<OutlineSymbol> Build(string buffer)
    {
        if (string.IsNullOrWhiteSpace(buffer))
            return [];

        var symbols = new List<OutlineSymbol>();

        IReadOnlyList<HclBlockHandle> handles;
        try
        {
            handles = HclReader.ReadOutline(buffer);
        }
        catch
        {
            return symbols;
        }

        foreach (var h in handles)
        {
            switch (h.Type)
            {
                case "locals":
                    symbols.Add(new OutlineSymbol(OutlineSymbolKind.Local, "locals", null, h.StartLine));
                    AppendLocals(buffer, h, symbols);
                    break;

                case "resource":
                    symbols.Add(new OutlineSymbol(
                        OutlineSymbolKind.Resource, JoinLabels(h), TypeLabel(h), h.StartLine));
                    break;

                case "data":
                    symbols.Add(new OutlineSymbol(
                        OutlineSymbolKind.DataSource, JoinLabels(h), TypeLabel(h), h.StartLine));
                    break;

                case "variable":
                    symbols.Add(new OutlineSymbol(OutlineSymbolKind.Variable, Label0(h, "variable"), null, h.StartLine));
                    break;

                case "output":
                    symbols.Add(new OutlineSymbol(OutlineSymbolKind.Output, Label0(h, "output"), null, h.StartLine));
                    break;

                case "module":
                    symbols.Add(new OutlineSymbol(OutlineSymbolKind.Module, Label0(h, "module"), null, h.StartLine));
                    break;

                case "provider":
                    symbols.Add(new OutlineSymbol(OutlineSymbolKind.Provider, Label0(h, "provider"), null, h.StartLine));
                    break;

                case "terraform":
                    symbols.Add(new OutlineSymbol(OutlineSymbolKind.Terraform, "terraform", null, h.StartLine));
                    break;

                case "moved":
                    symbols.Add(new OutlineSymbol(OutlineSymbolKind.Moved, "moved", null, h.StartLine));
                    break;

                case "import":
                    symbols.Add(new OutlineSymbol(OutlineSymbolKind.Import, "import", null, h.StartLine));
                    break;

                default:
                    symbols.Add(new OutlineSymbol(OutlineSymbolKind.Other, h.Descriptor, null, h.StartLine));
                    break;
            }
        }

        return symbols;
    }

    private static void AppendLocals(string buffer, HclBlockHandle handle, List<OutlineSymbol> symbols)
    {
        IReadOnlyList<HclParsedArgument> args;
        try
        {
            args = HclReader.ReadArguments(buffer, handle);
        }
        catch
        {
            return;
        }

        foreach (var a in args)
            symbols.Add(new OutlineSymbol(OutlineSymbolKind.Local, a.Name, "local", LineOf(buffer, a.ValueStart)));
    }

    private static string JoinLabels(HclBlockHandle h) =>
        h.Labels.Count >= 2 ? $"{h.Labels[0]}.{h.Labels[1]}"
        : h.Labels.Count == 1 ? h.Labels[0]
        : h.Type;

    private static string? TypeLabel(HclBlockHandle h) => h.Labels.Count >= 1 ? h.Labels[0] : null;

    private static string Label0(HclBlockHandle h, string fallback) =>
        h.Labels.Count >= 1 ? h.Labels[0] : fallback;

    private static int LineOf(string src, int offset)
    {
        var line = 1;
        var bound = Math.Min(offset, src.Length);
        for (var i = 0; i < bound; i++)
            if (src[i] == '\n') line++;
        return line;
    }
}
