using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PbiBench.Core.Report
{
    public enum PbirJsonKind { Object, Array, String, Number, True, False, Null }

    public sealed class PbirJsonException : FormatException
    {
        public PbirJsonException(string message, int offset) : base(message + " (offset " + offset + ")") { Offset = offset; }
        public int Offset { get; }
    }

    /// <summary>
    /// Immutable JSON node with its exact source span (Start inclusive, End exclusive, in chars of
    /// the parsed text). Spans let reviewed writes splice one value and keep every other byte.
    /// </summary>
    public sealed class PbirJsonNode
    {
        private static readonly KeyValuePair<string, PbirJsonNode>[] NoProperties = new KeyValuePair<string, PbirJsonNode>[0];
        private static readonly PbirJsonNode[] NoItems = new PbirJsonNode[0];

        internal PbirJsonNode(PbirJsonKind kind, int start) { Kind = kind; Start = start; }

        public PbirJsonKind Kind { get; }
        public int Start { get; }
        public int End { get; internal set; }
        /// <summary>Decoded string, raw number text, or null for other kinds.</summary>
        public string? Value { get; internal set; }
        public IReadOnlyList<KeyValuePair<string, PbirJsonNode>> Properties { get; internal set; } = NoProperties;
        public IReadOnlyList<PbirJsonNode> Items { get; internal set; } = NoItems;
        /// <summary>Span of each property's name token (same order as Properties); used to splice whole members.</summary>
        internal IReadOnlyList<int> PropertyNameStarts { get; set; } = new int[0];

        public bool IsObject => Kind == PbirJsonKind.Object;
        public bool IsArray => Kind == PbirJsonKind.Array;
        public bool IsString => Kind == PbirJsonKind.String;

        /// <summary>First property with this exact (ordinal) name; JSON from Power BI never repeats keys.</summary>
        public PbirJsonNode? Get(string name)
        {
            if (Kind != PbirJsonKind.Object) return null;
            foreach (var p in Properties) if (string.Equals(p.Key, name, StringComparison.Ordinal)) return p.Value;
            return null;
        }

        public string? GetString(string name) { var n = Get(name); return n != null && n.Kind == PbirJsonKind.String ? n.Value : null; }

        /// <summary>Follows a property path (a/b/c); null when any step is missing or not an object.</summary>
        public PbirJsonNode? Path(params string[] names)
        {
            PbirJsonNode? node = this;
            foreach (var name in names) { node = node?.Get(name); if (node == null) return null; }
            return node;
        }

        public string Raw(string text) => text.Substring(Start, End - Start);
    }

    /// <summary>
    /// Small bounded JSON reader for PBIR files (RFC 8259, no comments, no trailing commas). Chosen
    /// over Newtonsoft.Json in Core so PbiBench.Core keeps zero package dependencies, the net48 host
    /// binds only its own Newtonsoft copy, and source spans are exact for minimal splices.
    /// </summary>
    public static class PbirJson
    {
        public const int MaxDepth = 96;
        public const int MaxNodes = 2_000_000;
        public const int MaxChars = 32 * 1024 * 1024;

        public static PbirJsonNode Parse(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length > MaxChars) throw new PbirJsonException("JSON text exceeds the reader limit", 0);
            var reader = new Reader(text);
            reader.SkipWhitespace();
            var root = reader.ReadValue(0);
            reader.SkipWhitespace();
            if (reader.Position != text.Length) throw new PbirJsonException("Unexpected text after the JSON value", reader.Position);
            return root;
        }

        /// <summary>Decodes UTF-8 (BOM stripped, reported) for parsing; spans then refer to the returned text.</summary>
        public static string DecodeUtf8(byte[] bytes, out bool hadBom)
        {
            hadBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            return new UTF8Encoding(false, true).GetString(bytes, hadBom ? 3 : 0, bytes.Length - (hadBom ? 3 : 0));
        }

        public static byte[] EncodeUtf8(string text, bool withBom)
        {
            var body = new UTF8Encoding(false).GetBytes(text);
            if (!withBom) return body;
            var result = new byte[body.Length + 3];
            result[0] = 0xEF; result[1] = 0xBB; result[2] = 0xBF;
            Buffer.BlockCopy(body, 0, result, 3, body.Length);
            return result;
        }

        /// <summary>JSON string literal (quotes included) with the escapes Power BI itself writes.</summary>
        public static string Quote(string value)
        {
            var sb = new StringBuilder(value.Length + 2).Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }

        private sealed class Reader
        {
            private readonly string _text;
            private int _nodes;
            public int Position;

            public Reader(string text) { _text = text; }

            public void SkipWhitespace()
            {
                while (Position < _text.Length)
                {
                    var c = _text[Position];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') Position++; else break;
                }
            }

            private PbirJsonException Error(string message) => new PbirJsonException(message, Position);

            public PbirJsonNode ReadValue(int depth)
            {
                if (depth > MaxDepth) throw Error("JSON nesting exceeds " + MaxDepth + " levels");
                if (++_nodes > MaxNodes) throw Error("JSON has more than " + MaxNodes + " values");
                if (Position >= _text.Length) throw Error("Unexpected end of JSON");
                var c = _text[Position];
                switch (c)
                {
                    case '{': return ReadObject(depth);
                    case '[': return ReadArray(depth);
                    case '"':
                        {
                            var node = new PbirJsonNode(PbirJsonKind.String, Position);
                            node.Value = ReadString();
                            node.End = Position;
                            return node;
                        }
                    case 't': return ReadLiteral("true", PbirJsonKind.True);
                    case 'f': return ReadLiteral("false", PbirJsonKind.False);
                    case 'n': return ReadLiteral("null", PbirJsonKind.Null);
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                        throw Error("Unexpected character '" + c + "'");
                }
            }

            private PbirJsonNode ReadLiteral(string literal, PbirJsonKind kind)
            {
                if (string.CompareOrdinal(_text, Position, literal, 0, literal.Length) != 0) throw Error("Invalid literal");
                var node = new PbirJsonNode(kind, Position);
                Position += literal.Length;
                node.End = Position;
                return node;
            }

            private PbirJsonNode ReadNumber()
            {
                var start = Position;
                if (_text[Position] == '-') Position++;
                if (Position >= _text.Length) throw Error("Invalid number");
                if (_text[Position] == '0') Position++;
                else if (_text[Position] >= '1' && _text[Position] <= '9') while (Position < _text.Length && char.IsDigit(_text[Position]) && _text[Position] < 128) Position++;
                else throw Error("Invalid number");
                if (Position < _text.Length && _text[Position] == '.')
                {
                    Position++;
                    if (!Digits()) throw Error("Invalid number fraction");
                }
                if (Position < _text.Length && (_text[Position] == 'e' || _text[Position] == 'E'))
                {
                    Position++;
                    if (Position < _text.Length && (_text[Position] == '+' || _text[Position] == '-')) Position++;
                    if (!Digits()) throw Error("Invalid number exponent");
                }
                return new PbirJsonNode(PbirJsonKind.Number, start) { End = Position, Value = _text.Substring(start, Position - start) };
            }

            private bool Digits()
            {
                var start = Position;
                while (Position < _text.Length && _text[Position] >= '0' && _text[Position] <= '9') Position++;
                return Position > start;
            }

            private string ReadString()
            {
                Position++; // opening quote
                StringBuilder? sb = null;
                var runStart = Position;
                while (true)
                {
                    if (Position >= _text.Length) throw Error("Unterminated string");
                    var c = _text[Position];
                    if (c == '"')
                    {
                        string result;
                        if (sb == null) result = _text.Substring(runStart, Position - runStart);
                        else { sb.Append(_text, runStart, Position - runStart); result = sb.ToString(); }
                        Position++;
                        return result;
                    }
                    if (c < 0x20) throw Error("Control character in string");
                    if (c != '\\') { Position++; continue; }
                    sb ??= new StringBuilder();
                    sb.Append(_text, runStart, Position - runStart);
                    Position++;
                    if (Position >= _text.Length) throw Error("Unterminated escape");
                    var e = _text[Position++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (Position + 4 > _text.Length || !int.TryParse(_text.Substring(Position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                                throw Error("Invalid \\u escape");
                            sb.Append((char)code);
                            Position += 4;
                            break;
                        default: throw Error("Invalid escape '\\" + e + "'");
                    }
                    runStart = Position;
                }
            }

            private PbirJsonNode ReadObject(int depth)
            {
                var node = new PbirJsonNode(PbirJsonKind.Object, Position);
                var properties = new List<KeyValuePair<string, PbirJsonNode>>();
                var nameStarts = new List<int>();
                Position++;
                SkipWhitespace();
                if (Position < _text.Length && _text[Position] == '}') { Position++; node.End = Position; return node; }
                while (true)
                {
                    SkipWhitespace();
                    if (Position >= _text.Length || _text[Position] != '"') throw Error("Expected property name");
                    nameStarts.Add(Position);
                    var name = ReadString();
                    SkipWhitespace();
                    if (Position >= _text.Length || _text[Position] != ':') throw Error("Expected ':'");
                    Position++;
                    SkipWhitespace();
                    properties.Add(new KeyValuePair<string, PbirJsonNode>(name, ReadValue(depth + 1)));
                    SkipWhitespace();
                    if (Position >= _text.Length) throw Error("Unterminated object");
                    if (_text[Position] == ',') { Position++; continue; }
                    if (_text[Position] == '}') { Position++; break; }
                    throw Error("Expected ',' or '}'");
                }
                node.Properties = properties;
                node.PropertyNameStarts = nameStarts;
                node.End = Position;
                return node;
            }

            private PbirJsonNode ReadArray(int depth)
            {
                var node = new PbirJsonNode(PbirJsonKind.Array, Position);
                var items = new List<PbirJsonNode>();
                Position++;
                SkipWhitespace();
                if (Position < _text.Length && _text[Position] == ']') { Position++; node.End = Position; return node; }
                while (true)
                {
                    SkipWhitespace();
                    items.Add(ReadValue(depth + 1));
                    SkipWhitespace();
                    if (Position >= _text.Length) throw Error("Unterminated array");
                    if (_text[Position] == ',') { Position++; continue; }
                    if (_text[Position] == ']') { Position++; break; }
                    throw Error("Expected ',' or ']'");
                }
                node.Items = items;
                node.End = Position;
                return node;
            }
        }
    }
}
