using System;
using System.Collections.Generic;
using System.Linq;

namespace PbiBench.Core.Dax
{
    public static class DaxDiagnosticCodes
    {
        public const string Delimiter = "DAX001";
        public const string UnknownTable = "DAX002";
        public const string UnknownColumnOrMeasure = "DAX003";
        public const string UndeclaredVariable = "DAX004";
        public const string Arity = "DAX005";
        public const string UnknownFunction = "DAX006";

        /// <summary>Codes that describe a reference which does not resolve against the model.</summary>
        public static bool IsUnresolvedReference(string code) =>
            code == UnknownTable || code == UnknownColumnOrMeasure || code == UndeclaredVariable || code == UnknownFunction;
    }

    /// <summary>All diagnostics are advisory hints from local lexical analysis, never engine validation.</summary>
    public sealed class DaxDiagnostic
    {
        public const string AdvisoryLabel = "Advisory";
        public string Code { get; set; } = "";
        public int Start { get; set; }
        public int Length { get; set; }
        public string Message { get; set; } = "";
        public string Reference { get; set; } = "";
        public string Severity => AdvisoryLabel;
        public override string ToString() => Code + " (" + AdvisoryLabel + ") @" + Start + ": " + Message;
    }

    /// <summary>
    /// Advisory DAX checks: unbalanced delimiters and unterminated tokens; table, column/measure
    /// and function references that do not resolve against a fresh model name set; identifiers
    /// used like variables but never declared; argument counts outside the curated catalog
    /// range. Model-dependent checks run only when a name set with model objects is supplied.
    /// Heuristics prefer silence over false positives (query-defined names, string-named
    /// extension columns, VAR/UDF parameters and keyword arguments are all accepted).
    /// </summary>
    public static class DaxDiagnostics
    {
        public const int MaxDiagnostics = 200;

        // Bare words that are legal without parentheses: statement keywords, UDF type hints and
        // enumeration arguments (RANKX ties, CROSSFILTER direction, DATEDIFF intervals, window
        // ABS/REL, DATATABLE types, ORDER BY direction …).
        private static readonly HashSet<string> BareWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "VAR", "RETURN", "DEFINE", "EVALUATE", "MEASURE", "COLUMN", "TABLE", "FUNCTION", "ORDER", "BY", "START", "AT",
            "ASC", "DESC", "IN", "NOT", "AND", "OR", "TRUE", "FALSE", "CALENDAR",
            "SCALAR", "VAL", "EXPR", "ANYVAL", "ANYREF", "INT64", "DECIMAL", "DOUBLE", "STRING", "NUMERIC", "BOOLEAN",
            "DATETIME", "VARIANT", "INTEGER", "CURRENCY", "DATE", "TIME", "REF",
            "SECOND", "MINUTE", "HOUR", "DAY", "WEEK", "MONTH", "QUARTER", "YEAR",
            "SKIP", "DENSE", "BOTH", "ONEWAY", "NONE", "ONEWAY_LEFTFILTERSRIGHT", "ONEWAY_RIGHTFILTERSLEFT",
            "ABS", "REL", "KEEP", "FIRST", "LAST", "DEFAULT", "BLANKS", "PARTITIONS", "HIGHESTPARENT", "LOWESTPARENT"
        };

        public static IReadOnlyList<DaxDiagnostic> Analyze(string text, DaxModelNameSet? names = null, DaxSignatureCatalog? catalog = null)
        {
            text ??= "";
            names ??= DaxModelNameSet.Empty;
            catalog ??= DaxSignatureCatalog.Default;
            var tokens = DaxLexicalScanner.Scan(text);
            var code = tokens.Where(t => !t.IsTrivia).ToList();
            var result = new List<DaxDiagnostic>();
            CheckDelimiters(tokens, result);
            var q = QueryDeclarations.Collect(code);
            // A model UDF body is "(params) => expression": its parameters are declared names.
            var lambda = DaxSignatureCatalog.ParseUserDefined("lambda", text);
            if (lambda != null) foreach (var p in lambda.Parameters) q.Variables.Add(p.Name);
            var functions = catalog.With(q.Functions.Concat(names.Functions));
            CheckArity(code, functions, result);
            CheckVariableOrder(code, names, q, result);
            if (names.HasModel) CheckReferences(code, names, functions, q, result);
            return result.OrderBy(d => d.Start).ThenBy(d => d.Code, StringComparer.Ordinal).Take(MaxDiagnostics).ToArray();
        }

        private static void Add(List<DaxDiagnostic> list, string code, DaxLexToken token, string message, string reference = "")
        {
            if (list.Count >= MaxDiagnostics * 2) return;
            list.Add(new DaxDiagnostic { Code = code, Start = token.Start, Length = Math.Max(1, token.Length), Message = message + " (advisory)", Reference = reference });
        }

        private static void CheckDelimiters(IReadOnlyList<DaxLexToken> tokens, List<DaxDiagnostic> result)
        {
            var stack = new Stack<DaxLexToken>();
            foreach (var t in tokens)
            {
                if (!t.Terminated)
                {
                    var what = t.Kind == DaxLexKind.String ? "string" : t.Kind == DaxLexKind.QuotedTable ? "quoted table name" : t.Kind == DaxLexKind.Bracket ? "bracketed name" : "block comment";
                    Add(result, DaxDiagnosticCodes.Delimiter, t, "Unterminated " + what + ".");
                    continue;
                }
                if (t.Kind != DaxLexKind.Punctuation) continue;
                if (t.Text == "(" || t.Text == "{") stack.Push(t);
                else if (t.Text == ")" || t.Text == "}")
                {
                    var open = t.Text == ")" ? "(" : "{";
                    if (stack.Count == 0 || stack.Peek().Text != open) Add(result, DaxDiagnosticCodes.Delimiter, t, "Unmatched closing '" + t.Text + "'.");
                    else stack.Pop();
                }
            }
            foreach (var t in stack) Add(result, DaxDiagnosticCodes.Delimiter, t, "Unclosed '" + t.Text + "'.");
        }

        private static void CheckArity(List<DaxLexToken> code, DaxSignatureCatalog functions, List<DaxDiagnostic> result)
        {
            // Frame: function token (null for grouping/table constructors), separators seen, any argument token seen.
            var frames = new Stack<(DaxLexToken? fn, int commas, bool any)>();
            for (var i = 0; i < code.Count; i++)
            {
                var t = code[i];
                if (t.Kind == DaxLexKind.Punctuation && (t.Text == "(" || t.Text == "{"))
                {
                    if (frames.Count > 0) { var top = frames.Pop(); frames.Push((top.fn, top.commas, true)); }
                    var fn = t.Text == "(" && i > 0 && code[i - 1].Kind == DaxLexKind.Identifier && code[i - 1].End == t.Start ? code[i - 1] : null;
                    if (fn == null && t.Text == "(" && i > 0 && code[i - 1].Kind == DaxLexKind.Identifier && !IsKeyword(code[i - 1].Text)) fn = code[i - 1];
                    frames.Push((fn, 0, false));
                    continue;
                }
                if (t.Kind == DaxLexKind.Punctuation && (t.Text == ")" || t.Text == "}"))
                {
                    if (frames.Count == 0) continue;
                    var frame = frames.Pop();
                    if (frame.fn != null && functions.TryGet(frame.fn.Text, out var signature))
                    {
                        var count = frame.any || frame.commas > 0 ? frame.commas + 1 : 0;
                        if (!signature.AcceptsArgumentCount(count))
                        {
                            var range = signature.MaxArgs == DaxFunctionSignature.Unbounded ? signature.MinArgs + " or more"
                                : signature.MinArgs == signature.MaxArgs ? signature.MinArgs.ToString() : signature.MinArgs + "–" + signature.MaxArgs;
                            Add(result, DaxDiagnosticCodes.Arity, frame.fn, signature.Name + " received " + count + " argument(s); catalog expects " + range + ".", signature.Name);
                        }
                    }
                    continue;
                }
                if (frames.Count == 0) continue;
                var current = frames.Pop();
                if (t.Kind == DaxLexKind.Punctuation && (t.Text == "," || t.Text == ";")) frames.Push((current.fn, current.commas + 1, current.any));
                else frames.Push((current.fn, current.commas, true));
            }
        }

        private static bool IsKeyword(string word) => BareWords.Contains(word);

        /// <summary>
        /// A VAR is in scope only after its declaration (following VARs and RETURN of the same block), so a
        /// reference that precedes every declaration of that name can never resolve to it. Names that are
        /// also model/query tables, UDF parameters or query-level DEFINE VARs are not reported.
        /// </summary>
        private static void CheckVariableOrder(List<DaxLexToken> code, DaxModelNameSet names, QueryDeclarations q, List<DaxDiagnostic> result)
        {
            if (q.FirstVariableDeclaration.Count == 0) return;
            for (var i = 0; i < code.Count; i++)
            {
                var t = code[i];
                if (t.Kind != DaxLexKind.Identifier || q.DeclarationTokens.Contains(t)) continue;
                if (!q.FirstVariableDeclaration.TryGetValue(t.Text, out var declared) || declared <= t.Start) continue;
                var prev = i > 0 ? code[i - 1] : null;
                var next = i + 1 < code.Count ? code[i + 1] : null;
                if (next != null && next.Text == "(") continue;
                if (prev != null && (prev.Text == ":" || prev.Text == ".")) continue;
                if (q.QueryScopeVariables.Contains(t.Text) || q.Tables.Contains(t.Text) || names.IsTable(t.Text) || BareWords.Contains(t.Text)) continue;
                Add(result, DaxDiagnosticCodes.UndeclaredVariable, t, t.Text + " is used before its VAR declaration; a variable is only visible after it is declared.", t.Text);
            }
        }

        private static void CheckReferences(List<DaxLexToken> code, DaxModelNameSet names, DaxSignatureCatalog functions, QueryDeclarations q, List<DaxDiagnostic> result)
        {
            for (var i = 0; i < code.Count; i++)
            {
                var t = code[i];
                var prev = i > 0 ? code[i - 1] : null;
                var next = i + 1 < code.Count ? code[i + 1] : null;
                if (q.DeclarationTokens.Contains(t)) continue;

                if (t.Kind == DaxLexKind.QuotedTable && t.Terminated)
                {
                    if (!names.IsTable(t.Value) && !q.Tables.Contains(t.Value) && !q.Variables.Contains(t.Value))
                        Add(result, DaxDiagnosticCodes.UnknownTable, t, "Table " + DaxLexicalScanner.QuoteTable(t.Value) + " is not in the model.", t.Value);
                    continue;
                }

                if (t.Kind == DaxLexKind.Bracket && t.Terminated)
                {
                    var member = t.Value;
                    if (q.MemberNames.Contains(member) || names.IsMeasure(member)) continue;
                    var qualifier = prev != null && prev.End == t.Start && (prev.Kind == DaxLexKind.Identifier || (prev.Kind == DaxLexKind.QuotedTable && prev.Terminated)) ? prev : null;
                    if (qualifier != null)
                    {
                        var table = qualifier.Kind == DaxLexKind.QuotedTable ? qualifier.Value : qualifier.Text;
                        if (!names.IsTable(table)) continue; // unknown/query/variable tables are reported (or accepted) elsewhere
                        if (!names.TableHasMember(table, member))
                            Add(result, DaxDiagnosticCodes.UnknownColumnOrMeasure, t, DaxLexicalScanner.QuoteTable(table) + DaxLexicalScanner.QuoteColumn(member) + " is not a column or measure of that table.", table + "[" + member + "]");
                    }
                    else if (!names.IsAnyColumn(member))
                        Add(result, DaxDiagnosticCodes.UnknownColumnOrMeasure, t, DaxLexicalScanner.QuoteColumn(member) + " matches no model measure or column.", "[" + member + "]");
                    continue;
                }

                if (t.Kind != DaxLexKind.Identifier) continue;
                var word = t.Text;
                if (next != null && next.Text == "(" )
                {
                    if (BareWords.Contains(word) && !functions.TryGet(word, out _) && !names.InVocabulary(word)) continue;
                    if (names.HasVocabulary && !functions.TryGet(word, out _) && !names.InVocabulary(word))
                        Add(result, DaxDiagnosticCodes.UnknownFunction, t, word + " is not a catalog, pinned-vocabulary or user-defined function.", word);
                    continue;
                }
                if (next != null && next.Kind == DaxLexKind.Bracket && next.Start == t.End)
                {
                    if (!names.IsTable(word) && !q.Tables.Contains(word) && !q.Variables.Contains(word))
                        Add(result, DaxDiagnosticCodes.UnknownTable, t, "Table " + word + " is not in the model.", word);
                    continue;
                }
                if (prev != null && prev.Text == ":") continue; // UDF parameter type hints
                if (prev != null && prev.Kind == DaxLexKind.Identifier && (string.Equals(prev.Text, "ORDER", StringComparison.OrdinalIgnoreCase) || string.Equals(prev.Text, "START", StringComparison.OrdinalIgnoreCase))) continue;
                if (BareWords.Contains(word) || names.IsTable(word) || q.Tables.Contains(word) || q.Variables.Contains(word)) continue;
                if (functions.TryGet(word, out _) || names.InVocabulary(word)) continue;
                Add(result, DaxDiagnosticCodes.UndeclaredVariable, t, word + " is used like a variable or table but is not declared and is not a model table.", word);
            }
        }

        /// <summary>Names a query or expression defines for itself.</summary>
        private sealed class QueryDeclarations
        {
            public readonly HashSet<string> Variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> Tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> MemberNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<DaxLexToken> DeclarationTokens = new HashSet<DaxLexToken>();
            public readonly List<DaxFunctionSignature> Functions = new List<DaxFunctionSignature>();
            /// <summary>Start offset of the earliest expression-level VAR declaration of each name.</summary>
            public readonly Dictionary<string, int> FirstVariableDeclaration = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            public readonly HashSet<string> QueryScopeVariables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            public static QueryDeclarations Collect(List<DaxLexToken> code)
            {
                var q = new QueryDeclarations();
                var nesting = 0;
                bool defineSeen = false, evaluateSeen = false;
                for (var i = 0; i < code.Count; i++)
                {
                    var t = code[i];
                    var next = i + 1 < code.Count ? code[i + 1] : null;
                    if (t.Kind == DaxLexKind.Punctuation && (t.Text == "(" || t.Text == "{")) nesting++;
                    else if (t.Kind == DaxLexKind.Punctuation && (t.Text == ")" || t.Text == "}")) nesting = Math.Max(0, nesting - 1);
                    if (t.Kind == DaxLexKind.Identifier && nesting == 0)
                    {
                        if (string.Equals(t.Text, "DEFINE", StringComparison.OrdinalIgnoreCase)) defineSeen = true;
                        else if (string.Equals(t.Text, "EVALUATE", StringComparison.OrdinalIgnoreCase)) evaluateSeen = true;
                    }
                    if (t.Kind == DaxLexKind.Identifier && next != null && next.Kind == DaxLexKind.Identifier && string.Equals(t.Text, "VAR", StringComparison.OrdinalIgnoreCase))
                    {
                        // Query-level DEFINE VARs are visible to the whole query; expression VARs only after their declaration.
                        if (defineSeen && !evaluateSeen && nesting == 0) q.QueryScopeVariables.Add(next.Text);
                        else if (!q.FirstVariableDeclaration.TryGetValue(next.Text, out var start) || next.Start < start) q.FirstVariableDeclaration[next.Text] = next.Start;
                    }
                    if (t.Kind == DaxLexKind.String)
                    {
                        // Extension columns named by strings (ADDCOLUMNS, ROW, SELECTCOLUMNS, DATATABLE …),
                        // including "Table[Column]" spellings.
                        var value = t.Value;
                        q.MemberNames.Add(value);
                        var open = value.IndexOf('[');
                        if (open >= 0 && value.EndsWith("]", StringComparison.Ordinal)) q.MemberNames.Add(value.Substring(open + 1, value.Length - open - 2).Replace("]]", "]"));
                        continue;
                    }
                    if (t.Kind != DaxLexKind.Identifier || next == null) continue;
                    var word = t.Text.ToUpperInvariant();
                    if (word == "VAR" && next.Kind == DaxLexKind.Identifier) { q.Variables.Add(next.Text); q.DeclarationTokens.Add(next); }
                    else if (word == "TABLE" && (next.Kind == DaxLexKind.Identifier || next.Kind == DaxLexKind.QuotedTable) && i + 2 < code.Count && code[i + 2].Text == "=")
                    { q.Tables.Add(next.Kind == DaxLexKind.QuotedTable ? next.Value : next.Text); q.DeclarationTokens.Add(next); }
                    else if ((word == "MEASURE" || word == "COLUMN") && i + 2 < code.Count && code[i + 2].Kind == DaxLexKind.Bracket)
                    { q.MemberNames.Add(code[i + 2].Value); q.DeclarationTokens.Add(code[i + 2]); }
                    else if (word == "FUNCTION" && next.Kind == DaxLexKind.Identifier && i + 2 < code.Count && code[i + 2].Text == "=")
                    {
                        q.DeclarationTokens.Add(next);
                        var depth = 0;
                        var source = new System.Text.StringBuilder();
                        for (var j = i + 3; j < code.Count; j++)
                        {
                            source.Append(code[j].Text).Append(' ');
                            if (code[j].Text == "(") depth++;
                            else if (code[j].Text == ")" && --depth == 0) { if (j + 1 < code.Count) source.Append(code[j + 1].Text); break; }
                        }
                        var signature = DaxSignatureCatalog.ParseUserDefined(next.Text, source.ToString());
                        if (signature != null)
                        {
                            q.Functions.Add(signature);
                            foreach (var p in signature.Parameters) q.Variables.Add(p.Name);
                        }
                    }
                }
                return q;
            }
        }
    }
}
