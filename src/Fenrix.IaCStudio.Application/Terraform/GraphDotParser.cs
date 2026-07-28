using Fenrix.IaCStudio.Contracts.Terraform;

namespace Fenrix.IaCStudio.Application.Terraform;

/// <summary>
/// Parses the DOT emitted by <c>terraform graph</c> into a <see cref="DependencyGraph"/> for the visual
/// renderer. Handles Terraform's node-id conventions (a <c>[root] </c> prefix, <c>(expand)</c>/<c>(close)</c>
/// suffixes, and escaped quotes inside <c>provider["…"]</c> ids). Nodes are taken from explicit
/// declarations and any endpoints seen only in edges; each is classified by its label. This is pure text
/// parsing — <c>graph</c> output contains no sensitive values. See docs/25-execution-lifecycle.md.
/// </summary>
public static class GraphDotParser
{
    /// <summary>Parses DOT text; returns <see cref="DependencyGraph.Empty"/> for blank input.</summary>
    public static DependencyGraph Parse(string dot)
    {
        if (string.IsNullOrWhiteSpace(dot))
            return DependencyGraph.Empty;

        var nodes = new Dictionary<string, GraphNode>(StringComparer.Ordinal);
        var edges = new List<GraphEdge>();
        var edgeSeen = new HashSet<(string, string)>();

        foreach (var raw in dot.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] != '"')
                continue; // only node/edge statements start with a quoted id

            var arrow = FindArrow(line);
            if (arrow >= 0)
            {
                var from = FirstQuoted(line, 0);
                var to = FirstQuoted(line, arrow + 2);
                if (from is null || to is null)
                    continue;

                EnsureNode(nodes, from);
                EnsureNode(nodes, to);
                if (edgeSeen.Add((from, to)))
                    edges.Add(new GraphEdge(from, to));
            }
            else
            {
                // Node declaration: "ID" [label = "LABEL", shape = "box"]
                var id = FirstQuoted(line, 0);
                if (id is null)
                    continue;
                var label = ExtractLabel(line) ?? CleanId(id);
                nodes[id] = new GraphNode(id, label, Classify(label));
            }
        }

        return new DependencyGraph(nodes.Values.ToList(), edges);
    }

    private static void EnsureNode(Dictionary<string, GraphNode> nodes, string id)
    {
        if (nodes.ContainsKey(id))
            return;
        var label = CleanId(id);
        nodes[id] = new GraphNode(id, label, Classify(label));
    }

    /// <summary>Finds the index of a top-level <c>-&gt;</c> that is outside a quoted string.</summary>
    private static int FindArrow(string s)
    {
        var inQuote = false;
        for (var i = 0; i < s.Length - 1; i++)
        {
            var c = s[i];
            if (c == '\\') { i++; continue; } // skip escaped char
            if (c == '"') { inQuote = !inQuote; continue; }
            if (!inQuote && c == '-' && s[i + 1] == '>')
                return i;
        }
        return -1;
    }

    /// <summary>Extracts the first double-quoted token starting at or after <paramref name="start"/>, honoring <c>\"</c>.</summary>
    private static string? FirstQuoted(string s, int start)
    {
        var open = s.IndexOf('"', start);
        if (open < 0)
            return null;

        var sb = new System.Text.StringBuilder();
        for (var i = open + 1; i < s.Length; i++)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                sb.Append(s[i + 1]);
                i++;
                continue;
            }
            if (c == '"')
                return sb.ToString();
            sb.Append(c);
        }
        return null;
    }

    /// <summary>Extracts the value of the <c>label = "…"</c> attribute, if present.</summary>
    private static string? ExtractLabel(string line)
    {
        var idx = line.IndexOf("label", StringComparison.Ordinal);
        if (idx < 0)
            return null;
        var eq = line.IndexOf('=', idx);
        if (eq < 0)
            return null;
        return FirstQuoted(line, eq);
    }

    /// <summary>Strips the <c>[root] </c> prefix and the <c>(expand)</c>/<c>(close)</c> suffix from a node id.</summary>
    private static string CleanId(string id)
    {
        var s = id;
        if (s.StartsWith("[root] ", StringComparison.Ordinal))
            s = s["[root] ".Length..];
        else
        {
            var close = s.IndexOf("] ", StringComparison.Ordinal);
            if (s.StartsWith("[", StringComparison.Ordinal) && close > 0)
                s = s[(close + 2)..]; // strip any [module.x] style prefix
        }

        foreach (var suffix in new[] { " (expand)", " (close)", " (destroy)" })
            if (s.EndsWith(suffix, StringComparison.Ordinal))
                s = s[..^suffix.Length];

        return s.Trim();
    }

    private static GraphNodeKind Classify(string label)
    {
        if (label.StartsWith("data.", StringComparison.Ordinal)) return GraphNodeKind.DataSource;
        if (label.StartsWith("var.", StringComparison.Ordinal)) return GraphNodeKind.Variable;
        if (label.StartsWith("output.", StringComparison.Ordinal)) return GraphNodeKind.Output;
        if (label.StartsWith("local.", StringComparison.Ordinal)) return GraphNodeKind.Local;
        if (label.StartsWith("provider[", StringComparison.Ordinal) || label.Contains("provider[", StringComparison.Ordinal))
            return GraphNodeKind.Provider;
        if (label.StartsWith("module.", StringComparison.Ordinal)) return GraphNodeKind.Module;
        if (label is "root" or "") return GraphNodeKind.Other;
        return GraphNodeKind.Resource;
    }
}
