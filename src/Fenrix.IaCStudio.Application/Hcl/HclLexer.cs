using System.Collections.Generic;

namespace Fenrix.IaCStudio.Application.Hcl;

/// <summary>The lexical category of an <see cref="HclToken"/>.</summary>
public enum HclTokenKind
{
    Identifier,
    String,
    Number,
    Heredoc,
    LBrace, RBrace,
    LBracket, RBracket,
    LParen, RParen,
    Equals,
    Comma,
    /// <summary>A significant newline — HCL uses newlines to terminate arguments at block level.</summary>
    Newline,
    /// <summary>Any other operator/punctuation (<c>. : ? + - * / &lt; &gt; ! &amp; |</c>, etc.).</summary>
    Other,
    Eof
}

/// <summary>A lexed token. <see cref="Start"/> is inclusive, <see cref="End"/> exclusive, into the source string.</summary>
public readonly record struct HclToken(HclTokenKind Kind, int Start, int End, string Text);

/// <summary>
/// A pragmatic HCL lexer used by the round-trip reader. It is span-preserving (every token records its exact
/// source offsets) and correctly skips over the constructs that would otherwise confuse brace-matching:
/// quoted strings with <c>${…}</c>/<c>%{…}</c> interpolation and escapes, heredocs, and <c>#</c>/<c>//</c>/
/// <c>/* */</c> comments. It is not a full HCL parser — structural parsing happens in <see cref="HclReader"/>.
/// See docs/07-visual-builder.md.
/// </summary>
public static class HclLexer
{
    public static IReadOnlyList<HclToken> Tokenize(string src)
    {
        var tokens = new List<HclToken>();
        var i = 0;
        var n = src.Length;

        while (i < n)
        {
            var c = src[i];

            // Skip horizontal whitespace (newlines are significant).
            if (c is ' ' or '\t' or '\r')
            {
                i++;
                continue;
            }

            if (c == '\n')
            {
                tokens.Add(new HclToken(HclTokenKind.Newline, i, i + 1, "\n"));
                i++;
                continue;
            }

            // Comments.
            if (c == '#' || (c == '/' && i + 1 < n && src[i + 1] == '/'))
            {
                while (i < n && src[i] != '\n') i++;
                continue;
            }
            if (c == '/' && i + 1 < n && src[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < n && !(src[i] == '*' && src[i + 1] == '/')) i++;
                i = Math.Min(n, i + 2);
                continue;
            }

            // Heredoc: <<TAG or <<-TAG … TAG
            if (c == '<' && i + 1 < n && src[i + 1] == '<')
            {
                var end = ScanHeredoc(src, i);
                tokens.Add(new HclToken(HclTokenKind.Heredoc, i, end, src[i..end]));
                i = end;
                continue;
            }

            // Strings.
            if (c == '"')
            {
                var end = ScanString(src, i);
                tokens.Add(new HclToken(HclTokenKind.String, i, end, src[i..end]));
                i = end;
                continue;
            }

            // Numbers (allow a leading minus directly before a digit).
            if (char.IsDigit(c) || (c == '-' && i + 1 < n && char.IsDigit(src[i + 1])))
            {
                var end = ScanNumber(src, i);
                tokens.Add(new HclToken(HclTokenKind.Number, i, end, src[i..end]));
                i = end;
                continue;
            }

            // Identifiers.
            if (char.IsLetter(c) || c == '_')
            {
                var end = i + 1;
                while (end < n && (char.IsLetterOrDigit(src[end]) || src[end] is '_' or '-')) end++;
                tokens.Add(new HclToken(HclTokenKind.Identifier, i, end, src[i..end]));
                i = end;
                continue;
            }

            // Punctuation.
            var kind = c switch
            {
                '{' => HclTokenKind.LBrace,
                '}' => HclTokenKind.RBrace,
                '[' => HclTokenKind.LBracket,
                ']' => HclTokenKind.RBracket,
                '(' => HclTokenKind.LParen,
                ')' => HclTokenKind.RParen,
                ',' => HclTokenKind.Comma,
                '=' when !(i + 1 < n && src[i + 1] == '=') => HclTokenKind.Equals,
                _ => HclTokenKind.Other
            };
            tokens.Add(new HclToken(kind, i, i + 1, src[i..(i + 1)]));
            i++;
        }

        tokens.Add(new HclToken(HclTokenKind.Eof, n, n, string.Empty));
        return tokens;
    }

    /// <summary>Returns the index just past the closing quote of the string starting at <paramref name="i"/>.</summary>
    internal static int ScanString(string src, int i)
    {
        var n = src.Length;
        var j = i + 1;
        while (j < n)
        {
            var c = src[j];
            if (c == '\\') { j += 2; continue; }
            if (c == '"') return j + 1;
            if ((c == '$' || c == '%') && j + 1 < n && src[j + 1] == '{')
            {
                j = ScanInterpolation(src, j + 2);
                continue;
            }
            j++;
        }
        return n; // unterminated — treat rest as the string
    }

    /// <summary>Given an index just after <c>${</c>/<c>%{</c>, returns the index just past the matching <c>}</c>.</summary>
    private static int ScanInterpolation(string src, int k)
    {
        var n = src.Length;
        var depth = 1;
        while (k < n && depth > 0)
        {
            var c = src[k];
            if (c == '"') { k = ScanString(src, k); continue; }
            if (c == '{') { depth++; k++; }
            else if (c == '}') { depth--; k++; }
            else k++;
        }
        return k;
    }

    private static int ScanHeredoc(string src, int i)
    {
        var n = src.Length;
        var k = i + 2;
        if (k < n && src[k] == '-') k++;
        var tagStart = k;
        while (k < n && (char.IsLetterOrDigit(src[k]) || src[k] == '_')) k++;
        var tag = src[tagStart..k];
        if (tag.Length == 0)
            return Math.Min(n, i + 2); // malformed — bail

        // Advance to the terminator line whose trimmed content equals the tag.
        while (k < n)
        {
            // move to next line start
            while (k < n && src[k] != '\n') k++;
            if (k < n) k++; // consume newline
            var lineStart = k;
            while (k < n && src[k] != '\n') k++;
            var line = src[lineStart..k].Trim();
            if (line == tag)
                return Math.Min(n, k); // end at end of terminator line (before its newline)
        }
        return n;
    }

    private static int ScanNumber(string src, int i)
    {
        var n = src.Length;
        var j = i;
        if (src[j] == '-') j++;
        while (j < n && (char.IsDigit(src[j]) || src[j] == '.')) j++;
        // exponent
        if (j < n && (src[j] == 'e' || src[j] == 'E'))
        {
            j++;
            if (j < n && (src[j] == '+' || src[j] == '-')) j++;
            while (j < n && char.IsDigit(src[j])) j++;
        }
        return j;
    }
}
