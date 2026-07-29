using System.Text;

namespace Fenrix.IaCStudio.Application.Hcl;

/// <summary>A located top-level block in a source file: its type, labels, source span, and 1-based line range.</summary>
public sealed record HclBlockHandle(
    int Index,
    string Type,
    IReadOnlyList<string> Labels,
    int StartOffset,
    int EndOffset,
    int StartLine,
    int EndLine)
{
    /// <summary>Human descriptor, e.g. <c>resource "aws_instance" "web"</c>.</summary>
    public string Descriptor =>
        Labels.Count == 0 ? Type : $"{Type} {string.Join(" ", Labels.Select(l => $"\"{l}\""))}";
}

/// <summary>
/// A parsed direct argument of a block. <see cref="ValueStart"/>/<see cref="ValueEnd"/> are absolute offsets
/// into the original source so an edit can splice a new value in place, preserving everything else.
/// <see cref="IsSimple"/> is true only for plain literals the builder can present as a typed field.
/// </summary>
public sealed record HclParsedArgument(
    string Name,
    string RawValueText,
    int ValueStart,
    int ValueEnd,
    HclValue? Value,
    bool IsSimple);

/// <summary>
/// The round-trip half of the visual builder: locates top-level blocks and parses a block's direct arguments,
/// classifying each as a plain literal (editable) or a complex expression (preserved as raw source). It never
/// rewrites unsupported HCL — edits are applied as in-place value-span splices by the authoring service. This
/// is the "edit existing simple resource blocks / preserve unsupported HCL" contract of docs/07-visual-builder.md.
/// </summary>
public static class HclReader
{
    // ---- Outline ----

    public static IReadOnlyList<HclBlockHandle> ReadOutline(string src)
    {
        var tokens = HclLexer.Tokenize(src);
        var handles = new List<HclBlockHandle>();
        var p = 0;
        var index = 0;

        while (tokens[p].Kind != HclTokenKind.Eof)
        {
            var t = tokens[p];
            if (t.Kind is HclTokenKind.Newline or HclTokenKind.Comma)
            {
                p++;
                continue;
            }

            if (t.Kind == HclTokenKind.Identifier)
            {
                var blockType = t.Text;
                var startOffset = t.Start;
                p++;

                // Top-level `name = value` (e.g. tfvars) — skip, not a block.
                if (tokens[p].Kind == HclTokenKind.Equals)
                {
                    p = SkipToStatementEnd(tokens, p);
                    continue;
                }

                var labels = new List<string>();
                while (tokens[p].Kind is HclTokenKind.Identifier or HclTokenKind.String)
                {
                    labels.Add(Unquote(tokens[p].Text));
                    p++;
                }

                if (tokens[p].Kind == HclTokenKind.LBrace)
                {
                    var afterClose = SkipBraced(tokens, p);
                    var endOffset = tokens[afterClose - 1].End; // end of the matching RBrace
                    handles.Add(new HclBlockHandle(
                        index++, blockType, labels, startOffset, endOffset,
                        LineOf(src, startOffset), LineOf(src, endOffset - 1)));
                    p = afterClose;
                }
                // else: malformed header — fall through and keep scanning.
                continue;
            }

            p++;
        }

        return handles;
    }

    // ---- Arguments ----

    public static IReadOnlyList<HclParsedArgument> ReadArguments(string src, HclBlockHandle handle)
    {
        var baseOffset = handle.StartOffset;
        var sub = src[handle.StartOffset..handle.EndOffset];
        var tokens = HclLexer.Tokenize(sub);
        var args = new List<HclParsedArgument>();

        // Advance to the block's opening brace.
        var p = 0;
        while (tokens[p].Kind != HclTokenKind.LBrace && tokens[p].Kind != HclTokenKind.Eof) p++;
        if (tokens[p].Kind != HclTokenKind.LBrace) return args;
        p++; // past LBrace

        while (tokens[p].Kind is not (HclTokenKind.RBrace or HclTokenKind.Eof))
        {
            if (tokens[p].Kind is HclTokenKind.Newline or HclTokenKind.Comma)
            {
                p++;
                continue;
            }

            if (tokens[p].Kind is not (HclTokenKind.Identifier or HclTokenKind.String))
            {
                p++;
                continue;
            }

            var name = Unquote(tokens[p].Text);
            p++;

            if (tokens[p].Kind == HclTokenKind.Equals)
            {
                p++;
                // Allow the value to begin on the next line (e.g. `tags =\n  { … }`).
                while (tokens[p].Kind == HclTokenKind.Newline) p++;
                var valueTokens = new List<HclToken>();
                var depth = 0;
                while (true)
                {
                    var t = tokens[p];
                    if (t.Kind == HclTokenKind.Eof) break;
                    if (depth == 0 && t.Kind is HclTokenKind.Newline or HclTokenKind.Comma or HclTokenKind.RBrace)
                        break;
                    if (t.Kind is HclTokenKind.LBrace or HclTokenKind.LBracket or HclTokenKind.LParen) depth++;
                    else if (t.Kind is HclTokenKind.RBrace or HclTokenKind.RBracket or HclTokenKind.RParen) depth--;
                    valueTokens.Add(t);
                    p++;
                }

                if (valueTokens.Count > 0)
                {
                    var vStart = valueTokens[0].Start;
                    var vEnd = valueTokens[^1].End;
                    var raw = sub[vStart..vEnd];
                    var (value, simple) = Classify(valueTokens, sub);
                    args.Add(new HclParsedArgument(name, raw, baseOffset + vStart, baseOffset + vEnd, value, simple));
                }
            }
            else
            {
                // Nested block: skip labels then the braced body.
                while (tokens[p].Kind is HclTokenKind.Identifier or HclTokenKind.String) p++;
                if (tokens[p].Kind == HclTokenKind.LBrace)
                    p = SkipBraced(tokens, p);
                else
                    p++;
            }
        }

        return args;
    }

    // ---- Value classification ----

    private static (HclValue? Value, bool Simple) Classify(IReadOnlyList<HclToken> tokens, string src)
    {
        // Trim leading/trailing newlines so the first/last tokens are the real value delimiters, but keep
        // interior newlines — object literals may separate entries with newlines rather than commas.
        var trimmed = TrimNewlines(tokens);
        if (trimmed.Count == 0)
            return (null, false);

        var raw = src[trimmed[0].Start..trimmed[^1].End];
        var sig = trimmed.Where(t => t.Kind != HclTokenKind.Newline).ToList();
        if (sig.Count == 0)
            return (null, false);

        if (sig.Count == 1)
        {
            var t = sig[0];
            switch (t.Kind)
            {
                case HclTokenKind.String:
                    if (t.Text.Contains("${") || t.Text.Contains("%{"))
                        return (HclValue.Raw(t.Text), false);
                    return (new HclString(DecodeString(t.Text)), true);
                case HclTokenKind.Number:
                    return (new HclNumber(t.Text), true);
                case HclTokenKind.Identifier:
                    return t.Text switch
                    {
                        "true" => (new HclBool(true), true),
                        "false" => (new HclBool(false), true),
                        "null" => (HclValue.Null, true),
                        _ => (HclValue.Raw(t.Text), false)
                    };
                default:
                    return (HclValue.Raw(raw), false);
            }
        }

        var first = sig[0];
        var last = sig[^1];

        if (first.Kind == HclTokenKind.LBracket && last.Kind == HclTokenKind.RBracket)
        {
            var inner = Slice(trimmed, 1, trimmed.Count - 1);
            var segments = SplitTopLevel(inner, commasOnly: true);
            var items = new List<HclValue>();
            foreach (var seg in segments)
            {
                if (seg.Count == 0) continue;
                var (v, s) = Classify(seg, src);
                if (!s || v is null)
                    return (HclValue.Raw(raw), false);
                items.Add(v);
            }
            return (new HclList(items), true);
        }

        if (first.Kind == HclTokenKind.LBrace && last.Kind == HclTokenKind.RBrace)
        {
            var inner = Slice(trimmed, 1, trimmed.Count - 1);
            var entries = SplitTopLevel(inner, commasOnly: false);
            var result = new List<KeyValuePair<string, HclValue>>();
            foreach (var entry in entries)
            {
                if (entry.Count == 0) continue;
                // key = value
                if (entry.Count < 2 || entry[1].Kind != HclTokenKind.Equals ||
                    entry[0].Kind is not (HclTokenKind.Identifier or HclTokenKind.String))
                    return (HclValue.Raw(raw), false);
                var key = Unquote(entry[0].Text);
                var (v, s) = Classify(Slice(entry, 2, entry.Count), src);
                if (!s || v is null)
                    return (HclValue.Raw(raw), false);
                result.Add(new KeyValuePair<string, HclValue>(key, v));
            }
            return (new HclObject(result), true);
        }

        return (HclValue.Raw(raw), false);
    }

    /// <summary>Splits a token run on top-level commas (and, for objects, newlines) ignoring nested brackets.</summary>
    private static List<List<HclToken>> SplitTopLevel(IReadOnlyList<HclToken> tokens, bool commasOnly)
    {
        var segments = new List<List<HclToken>>();
        var current = new List<HclToken>();
        var depth = 0;
        foreach (var t in tokens)
        {
            if (t.Kind is HclTokenKind.LBrace or HclTokenKind.LBracket or HclTokenKind.LParen) depth++;
            else if (t.Kind is HclTokenKind.RBrace or HclTokenKind.RBracket or HclTokenKind.RParen) depth--;

            var isSeparator = depth == 0 && (t.Kind == HclTokenKind.Comma || (!commasOnly && t.Kind == HclTokenKind.Newline));
            if (isSeparator)
            {
                if (current.Count > 0) segments.Add(current);
                current = [];
                continue;
            }
            if (t.Kind == HclTokenKind.Newline) continue; // ignore newlines inside a segment
            current.Add(t);
        }
        if (current.Count > 0) segments.Add(current);
        return segments;
    }

    private static List<HclToken> Slice(IReadOnlyList<HclToken> tokens, int start, int end)
    {
        var list = new List<HclToken>(Math.Max(0, end - start));
        for (var i = start; i < end && i < tokens.Count; i++) list.Add(tokens[i]);
        return list;
    }

    /// <summary>Removes leading and trailing <see cref="HclTokenKind.Newline"/> tokens, keeping interior ones.</summary>
    private static List<HclToken> TrimNewlines(IReadOnlyList<HclToken> tokens)
    {
        var start = 0;
        var end = tokens.Count;
        while (start < end && tokens[start].Kind == HclTokenKind.Newline) start++;
        while (end > start && tokens[end - 1].Kind == HclTokenKind.Newline) end--;
        return Slice(tokens, start, end);
    }

    // ---- helpers ----

    /// <summary>Given <paramref name="p"/> at an LBrace token, returns the index just past the matching RBrace.</summary>
    private static int SkipBraced(IReadOnlyList<HclToken> tokens, int p)
    {
        var depth = 0;
        while (tokens[p].Kind != HclTokenKind.Eof)
        {
            if (tokens[p].Kind == HclTokenKind.LBrace) depth++;
            else if (tokens[p].Kind == HclTokenKind.RBrace)
            {
                depth--;
                if (depth == 0) return p + 1;
            }
            p++;
        }
        return p;
    }

    private static int SkipToStatementEnd(IReadOnlyList<HclToken> tokens, int p)
    {
        var depth = 0;
        while (tokens[p].Kind != HclTokenKind.Eof)
        {
            var t = tokens[p];
            if (depth == 0 && t.Kind == HclTokenKind.Newline) return p + 1;
            if (t.Kind is HclTokenKind.LBrace or HclTokenKind.LBracket or HclTokenKind.LParen) depth++;
            else if (t.Kind is HclTokenKind.RBrace or HclTokenKind.RBracket or HclTokenKind.RParen) depth--;
            p++;
        }
        return p;
    }

    internal static string Unquote(string token)
    {
        if (token.Length >= 2 && token[0] == '"' && token[^1] == '"')
            return DecodeString(token);
        return token;
    }

    internal static string DecodeString(string token)
    {
        var body = token.Length >= 2 && token[0] == '"' && token[^1] == '"' ? token[1..^1] : token;
        var sb = new StringBuilder(body.Length);
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '\\' && i + 1 < body.Length)
            {
                var next = body[++i];
                sb.Append(next switch
                {
                    'n' => '\n',
                    't' => '\t',
                    'r' => '\r',
                    '"' => '"',
                    '\\' => '\\',
                    _ => next
                });
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static int LineOf(string src, int offset)
    {
        var line = 1;
        var bound = Math.Min(offset, src.Length);
        for (var i = 0; i < bound; i++)
            if (src[i] == '\n') line++;
        return line;
    }
}
