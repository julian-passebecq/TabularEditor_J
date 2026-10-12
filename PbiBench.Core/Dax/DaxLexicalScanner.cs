using System;
using System.Collections.Generic;
using System.Text;

namespace PbiBench.Core.Dax
{
    public enum DaxLexKind
    {
        Whitespace,
        LineComment,
        BlockComment,
        String,
        QuotedTable,
        Bracket,
        Identifier,
        Number,
        Punctuation,
        Unknown
    }

    /// <summary>
    /// One lexical token. Start/Length are raw text positions; Value is the unescaped content
    /// for strings ("" -> "), quoted tables ('' -> ') and bracket names (]] -> ]).
    /// Terminated is false when the closing delimiter is missing (caret inside an open token).
    /// </summary>
    public sealed class DaxLexToken
    {
        public DaxLexKind Kind { get; set; }
        public int Start { get; set; }
        public int Length { get; set; }
        public string Text { get; set; } = "";
        public string Value { get; set; } = "";
        public bool Terminated { get; set; } = true;
        public int End => Start + Length;
        public bool IsTrivia => Kind == DaxLexKind.Whitespace || Kind == DaxLexKind.LineComment || Kind == DaxLexKind.BlockComment;
    }

    /// <summary>
    /// Neutral lexical scanner mirroring the token shapes of TE2's pinned DAXLexer.g4 (nested
    /// block comments, // and -- line comments, "" / '' / ]] escapes, dotted identifiers such as
    /// PERCENTILE.INC or UDF names). It is a tokenizer only: no grammar, binder or engine
    /// validation. The host smoke cross-checks delimiter results with the TE2 lexer.
    /// </summary>
    public static class DaxLexicalScanner
    {
        public static IReadOnlyList<DaxLexToken> Scan(string text)
        {
            text ??= "";
            if (text.Length > DaxAuthoring.MaxTextLength)
                throw new ArgumentException("Local assistance is limited to 256 Ki characters.");
            var tokens = new List<DaxLexToken>();
            var i = 0;
            while (i < text.Length)
            {
                var c = text[i];
                var start = i;
                if (char.IsWhiteSpace(c))
                {
                    while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
                    tokens.Add(Make(text, DaxLexKind.Whitespace, start, i, null, true));
                }
                else if ((c == '/' && Peek(text, i + 1) == '/') || (c == '-' && Peek(text, i + 1) == '-'))
                {
                    while (i < text.Length && text[i] != '\r' && text[i] != '\n' && !IsLineBreak(text[i])) i++;
                    tokens.Add(Make(text, DaxLexKind.LineComment, start, i, null, true));
                }
                else if (c == '/' && Peek(text, i + 1) == '*')
                {
                    var depth = 0;
                    var terminated = false;
                    while (i < text.Length)
                    {
                        if (text[i] == '/' && Peek(text, i + 1) == '*') { depth++; i += 2; continue; }
                        if (text[i] == '*' && Peek(text, i + 1) == '/')
                        {
                            depth--; i += 2;
                            if (depth == 0) { terminated = true; break; }
                            continue;
                        }
                        i++;
                    }
                    tokens.Add(Make(text, DaxLexKind.BlockComment, start, i, null, terminated));
                }
                else if (c == '"')
                {
                    var value = ReadQuoted(text, ref i, '"', '"', false, out var terminated);
                    tokens.Add(Make(text, DaxLexKind.String, start, i, value, terminated));
                }
                else if (c == '\'')
                {
                    var value = ReadQuoted(text, ref i, '\'', '\'', true, out var terminated);
                    tokens.Add(Make(text, DaxLexKind.QuotedTable, start, i, value, terminated));
                }
                else if (c == '[')
                {
                    var value = ReadQuoted(text, ref i, '[', ']', true, out var terminated);
                    tokens.Add(Make(text, DaxLexKind.Bracket, start, i, value, terminated));
                }
                else if (IsIdentifierStart(c))
                {
                    i++;
                    while (i < text.Length)
                    {
                        if (IsIdentifierPart(text[i])) { i++; continue; }
                        // Dotted names (VAR.P, PERCENTILE.INC, Lib.Function) continue with a letter.
                        if (text[i] == '.' && i + 1 < text.Length && IsIdentifierStart(text[i + 1])) { i++; continue; }
                        break;
                    }
                    tokens.Add(Make(text, DaxLexKind.Identifier, start, i, null, true));
                }
                else if (char.IsDigit(c) || (c == '.' && char.IsDigit(Peek(text, i + 1))))
                {
                    while (i < text.Length && (char.IsDigit(text[i]) || text[i] == '.')) i++;
                    if (i < text.Length && (text[i] == 'e' || text[i] == 'E'))
                    {
                        var j = i + 1;
                        if (j < text.Length && (text[j] == '+' || text[j] == '-')) j++;
                        if (j < text.Length && char.IsDigit(text[j])) { i = j; while (i < text.Length && char.IsDigit(text[i])) i++; }
                    }
                    tokens.Add(Make(text, DaxLexKind.Number, start, i, null, true));
                }
                else
                {
                    var two = i + 1 < text.Length ? text.Substring(i, 2) : "";
                    if (two == "&&" || two == "||" || two == "<>" || two == "<=" || two == ">=" || two == "=>" || two == "==") i += 2;
                    else i++;
                    var kind = "(){},;+-*/^&|=<>:.!".IndexOf(c) >= 0 ? DaxLexKind.Punctuation : DaxLexKind.Unknown;
                    tokens.Add(Make(text, kind, start, i, null, true));
                }
            }
            return tokens;
        }

        /// <summary>Adapter for the existing structure/parameter helpers.</summary>
        public static IReadOnlyList<DaxAuthoringToken> ToAuthoringTokens(IEnumerable<DaxLexToken> tokens)
        {
            var result = new List<DaxAuthoringToken>();
            foreach (var t in tokens)
                result.Add(new DaxAuthoringToken {
                    Text = t.Kind == DaxLexKind.Punctuation || t.Kind == DaxLexKind.Identifier ? t.Text : t.Value,
                    Start = t.Start, Length = t.Length,
                    IsTriviaOrLiteral = t.Kind != DaxLexKind.Punctuation && t.Kind != DaxLexKind.Identifier && t.Kind != DaxLexKind.Number && t.Kind != DaxLexKind.Unknown
                });
            return result;
        }

        public static bool IsIdentifierStart(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';
        public static bool IsIdentifierPart(char c) => IsIdentifierStart(c) || (c >= '0' && c <= '9');

        /// <summary>Exact DAX quoting of a table name: always quoted, ' doubled.</summary>
        public static string QuoteTable(string name) => "'" + (name ?? "").Replace("'", "''") + "'";

        /// <summary>Exact DAX bracket quoting of a column or measure name: ] doubled.</summary>
        public static string QuoteColumn(string name) => "[" + (name ?? "").Replace("]", "]]") + "]";

        // NEL, LINE SEPARATOR and PARAGRAPH SEPARATOR end line comments like CR/LF (DAXLexer.g4).
        private static bool IsLineBreak(char c) => c == '\r' || c == '\n' || c == (char)0x85 || c == (char)0x2028 || c == (char)0x2029;

        private static char Peek(string text, int index) => index < text.Length ? text[index] : '\0';

        private static string ReadQuoted(string text, ref int i, char open, char close, bool stopAtNewLine, out bool terminated)
        {
            var sb = new StringBuilder();
            i++; // opener
            terminated = false;
            while (i < text.Length)
            {
                var ch = text[i];
                if (ch == close)
                {
                    if (Peek(text, i + 1) == close) { sb.Append(close); i += 2; continue; }
                    i++; terminated = true; break;
                }
                if (stopAtNewLine && (ch == '\r' || ch == '\n')) break;
                sb.Append(ch);
                i++;
            }
            return sb.ToString();
        }

        private static DaxLexToken Make(string text, DaxLexKind kind, int start, int end, string? value, bool terminated)
        {
            var raw = text.Substring(start, end - start);
            return new DaxLexToken { Kind = kind, Start = start, Length = end - start, Text = raw, Value = value ?? raw, Terminated = terminated };
        }
    }
}
