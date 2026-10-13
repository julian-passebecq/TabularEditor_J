using System;
using System.Collections.Generic;
using System.Linq;

namespace PbiBench.Core.Dax
{
    public enum DaxCaretKind
    {
        /// <summary>Code position: functions, variables, tables, measures, keywords.</summary>
        Code,
        /// <summary>Inside a string literal: no completion.</summary>
        InsideString,
        /// <summary>Inside a line or block comment: no completion.</summary>
        InsideComment,
        /// <summary>Inside 'Table: table names.</summary>
        InsideQuotedTable,
        /// <summary>Inside 'Table'[ or Table[: columns and measures of that table.</summary>
        InsideTableColumn,
        /// <summary>Inside a bare [: measures and columns.</summary>
        InsideBracket,
        /// <summary>Naming a new VAR: no completion.</summary>
        VariableDeclaration
    }

    /// <summary>
    /// Caret context computed from the neutral lexical scanner. ReplaceStart/ReplaceLength is the
    /// exact text range a completion replaces (the partial identifier or the whole open
    /// bracket/quoted token), so insertion never leaves half a name behind.
    /// </summary>
    public sealed class DaxCompletionContext
    {
        private static readonly string[] Keywords = {
            "VAR", "RETURN", "DEFINE", "EVALUATE", "MEASURE", "COLUMN", "TABLE", "FUNCTION",
            "ORDER BY", "START AT", "ASC", "DESC", "IN", "NOT"
        };

        public DaxCaretKind Kind { get; private set; }
        public string Prefix { get; private set; } = "";
        public int ReplaceStart { get; private set; }
        public int ReplaceLength { get; private set; }
        public string TableName { get; private set; } = "";
        public string EnclosingFunction { get; private set; } = "";
        public int ArgumentIndex { get; private set; }
        public bool AfterFunctionOpener { get; private set; }
        public IReadOnlyList<string> Variables { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<DaxFunctionSignature> QueryFunctions { get; private set; } = Array.Empty<DaxFunctionSignature>();
        public bool AllowsCompletion => Kind != DaxCaretKind.InsideString && Kind != DaxCaretKind.InsideComment && Kind != DaxCaretKind.VariableDeclaration;

        public static DaxCompletionContext Analyze(string text, int caret)
        {
            text ??= "";
            caret = Math.Max(0, Math.Min(caret, text.Length));
            var tokens = DaxLexicalScanner.Scan(text);
            var ctx = new DaxCompletionContext { Kind = DaxCaretKind.Code, ReplaceStart = caret };
            DaxLexToken? current = null;
            foreach (var t in tokens)
            {
                if (t.Start >= caret) break;
                var inside = caret < t.End || (caret == t.End && (!t.Terminated || t.Kind == DaxLexKind.Identifier || t.Kind == DaxLexKind.LineComment));
                if (inside) { current = t; break; }
            }

            var before = tokens.Where(t => !t.IsTrivia && t.End <= caret && !ReferenceEquals(t, current)).ToList();
            var previous = current == null ? before.LastOrDefault() : before.LastOrDefault(t => t.End <= current.Start);
            ComputeCallFrame(ctx, before);
            ctx.AfterFunctionOpener = previous != null && (previous.Text == "(" || previous.Text == "," || previous.Text == ";");
            ctx.Variables = Declarations(tokens, caret, out var functions);
            ctx.QueryFunctions = functions;

            if (current != null)
            {
                switch (current.Kind)
                {
                    case DaxLexKind.String: ctx.Kind = DaxCaretKind.InsideString; return ctx;
                    case DaxLexKind.LineComment:
                    case DaxLexKind.BlockComment: ctx.Kind = DaxCaretKind.InsideComment; return ctx;
                    case DaxLexKind.QuotedTable:
                        ctx.Kind = DaxCaretKind.InsideQuotedTable;
                        ctx.Prefix = Unescape(text.Substring(current.Start + 1, caret - current.Start - 1), '\'');
                        ctx.ReplaceStart = current.Start;
                        ctx.ReplaceLength = current.Terminated ? current.Length : caret - current.Start;
                        return ctx;
                    case DaxLexKind.Bracket:
                        ctx.Prefix = Unescape(text.Substring(current.Start + 1, caret - current.Start - 1), ']');
                        ctx.ReplaceStart = current.Start;
                        ctx.ReplaceLength = current.Terminated ? current.Length : caret - current.Start;
                        var qualifier = before.LastOrDefault();
                        if (qualifier != null && qualifier.End == current.Start && qualifier.Kind == DaxLexKind.QuotedTable && qualifier.Terminated)
                        { ctx.Kind = DaxCaretKind.InsideTableColumn; ctx.TableName = qualifier.Value; }
                        else if (qualifier != null && qualifier.End == current.Start && qualifier.Kind == DaxLexKind.Identifier)
                        { ctx.Kind = DaxCaretKind.InsideTableColumn; ctx.TableName = qualifier.Text; }
                        else ctx.Kind = DaxCaretKind.InsideBracket;
                        return ctx;
                    case DaxLexKind.Identifier:
                        ctx.Prefix = text.Substring(current.Start, caret - current.Start);
                        ctx.ReplaceStart = current.Start;
                        ctx.ReplaceLength = current.Length;
                        if (IsWord(previous, "VAR")) ctx.Kind = DaxCaretKind.VariableDeclaration;
                        return ctx;
                }
            }
            if (IsWord(previous, "VAR")) ctx.Kind = DaxCaretKind.VariableDeclaration;
            return ctx;
        }

        /// <summary>Ranked completion items for this context; InsertText replaces ReplaceStart/Length.</summary>
        public IReadOnlyList<DaxAuthoringItem> Suggest(DaxModelNameSet? names, DaxSignatureCatalog? catalog)
        {
            if (!AllowsCompletion) return Array.Empty<DaxAuthoringItem>();
            names ??= DaxModelNameSet.Empty;
            catalog ??= DaxSignatureCatalog.Default;
            var items = new List<DaxAuthoringItem>();
            switch (Kind)
            {
                case DaxCaretKind.InsideTableColumn:
                    var table = names.GetTable(TableName);
                    if (table != null)
                    {
                        foreach (var c in table.Columns) items.Add(Item(c, DaxLexicalScanner.QuoteColumn(c), "Column", DaxLexicalScanner.QuoteTable(table.Name)));
                        foreach (var m in table.Measures) items.Add(Item(m, DaxLexicalScanner.QuoteColumn(m), "Measure", DaxLexicalScanner.QuoteTable(table.Name)));
                    }
                    break;
                case DaxCaretKind.InsideBracket:
                    foreach (var t in names.Tables)
                        foreach (var m in t.Measures) items.Add(Item(m, DaxLexicalScanner.QuoteColumn(m), "Measure", DaxLexicalScanner.QuoteTable(t.Name)));
                    foreach (var group in names.Tables.SelectMany(t => t.Columns.Select(c => new { t, c })).GroupBy(x => x.c, StringComparer.OrdinalIgnoreCase))
                        items.Add(Item(group.Key, DaxLexicalScanner.QuoteColumn(group.Key), "Column", string.Join(", ", group.Select(x => x.t.Name))));
                    break;
                case DaxCaretKind.InsideQuotedTable:
                    foreach (var t in names.Tables) items.Add(Item(t.Name, DaxLexicalScanner.QuoteTable(t.Name), "Table", t.IsHidden ? "hidden" : ""));
                    break;
                default:
                    foreach (var v in Variables) items.Add(Item(v, v, "Variable", "Declared before the caret"));
                    foreach (var f in QueryFunctions.Concat(names.Functions)) items.Add(Item(f.Name, f.Name + "(", "Function", f.Display));
                    foreach (var f in catalog.Entries) items.Add(Item(f.Name, f.Name + "(", "Function", f.Display + " · " + f.Summary));
                    var catalogKeys = new HashSet<string>(catalog.Entries.Select(e => DaxSignatureCatalog.VocabularyKey(e.Name)), StringComparer.Ordinal);
                    foreach (var v in names.Vocabulary)
                        if (!catalogKeys.Contains(DaxSignatureCatalog.VocabularyKey(v)))
                            items.Add(Item(v, v + "(", "Function", "Pinned TE2 vocabulary; no local signature."));
                    foreach (var t in names.Tables) items.Add(Item(t.Name, DaxLexicalScanner.QuoteTable(t.Name), "Table", t.IsHidden ? "hidden" : ""));
                    foreach (var t in names.Tables)
                        foreach (var m in t.Measures) items.Add(Item(m, DaxLexicalScanner.QuoteColumn(m), "Measure", DaxLexicalScanner.QuoteTable(t.Name)));
                    foreach (var k in Keywords) items.Add(Item(k, k + " ", "Keyword", ""));
                    break;
            }
            var prefix = Prefix.Trim();
            var unique = new HashSet<string>(StringComparer.Ordinal);
            return items.Where(x => prefix.Length == 0 || x.Name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0)
                .Where(x => unique.Add(x.Kind + "\u0001" + x.InsertText))
                .OrderByDescending(x => prefix.Length > 0 && x.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => KindRank(x.Kind))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Take(DaxAuthoring.MaxSuggestions).ToArray();
        }

        /// <summary>Signature line for the enclosing call, active parameter wrapped in « ».</summary>
        public string SignatureHelp(DaxModelNameSet? names, DaxSignatureCatalog? catalog)
        {
            if (EnclosingFunction.Length == 0) return "No enclosing function call.";
            var signature = QueryFunctions.FirstOrDefault(f => string.Equals(f.Name, EnclosingFunction, StringComparison.OrdinalIgnoreCase))
                ?? names?.GetFunction(EnclosingFunction);
            if (signature == null && (catalog ?? DaxSignatureCatalog.Default).TryGet(EnclosingFunction, out var found)) signature = found;
            if (signature == null) return EnclosingFunction + " · argument " + ArgumentIndex + " · no local signature (not a validity check).";
            var tooMany = signature.ActiveParameter(ArgumentIndex) < 0 && ArgumentIndex > 0;
            return signature.Format(ArgumentIndex) + " · argument " + ArgumentIndex + (tooMany ? " (beyond expected arguments)" : "")
                + (signature.Summary.Length > 0 ? " · " + signature.Summary : "");
        }

        private static int KindRank(string kind)
        {
            switch (kind)
            {
                case "Variable": return 0;
                case "Measure": return 1;
                case "Column": return 2;
                case "Table": return 3;
                case "Function": return 4;
                default: return 5;
            }
        }

        private static DaxAuthoringItem Item(string name, string insert, string kind, string detail) =>
            new DaxAuthoringItem { Name = name, InsertText = insert, Kind = kind, Detail = detail };

        private static bool IsWord(DaxLexToken? token, string word) =>
            token != null && token.Kind == DaxLexKind.Identifier && string.Equals(token.Text, word, StringComparison.OrdinalIgnoreCase);

        private static string Unescape(string raw, char close) => raw.Replace(new string(close, 2), close.ToString());

        private static void ComputeCallFrame(DaxCompletionContext ctx, List<DaxLexToken> before)
        {
            var frames = new Stack<Tuple<string, int>>();
            DaxLexToken? prev = null;
            foreach (var t in before)
            {
                if (t.Text == "(" || t.Text == "{")
                    frames.Push(Tuple.Create(t.Text == "(" && prev != null && prev.Kind == DaxLexKind.Identifier ? prev.Text : "", 1));
                else if (t.Text == ")" || t.Text == "}") { if (frames.Count > 0) frames.Pop(); }
                else if ((t.Text == "," || t.Text == ";") && frames.Count > 0)
                {
                    var f = frames.Pop();
                    frames.Push(Tuple.Create(f.Item1, f.Item2 + 1));
                }
                prev = t;
            }
            foreach (var frame in frames)
                if (frame.Item1.Length > 0) { ctx.EnclosingFunction = frame.Item1; ctx.ArgumentIndex = frame.Item2; return; }
        }

        /// <summary>VAR names and query FUNCTION definitions (with parameters as variables) before the caret.</summary>
        internal static IReadOnlyList<string> Declarations(IReadOnlyList<DaxLexToken> tokens, int caret, out IReadOnlyList<DaxFunctionSignature> functions)
        {
            var names = new List<string>();
            var udfs = new List<DaxFunctionSignature>();
            var code = tokens.Where(t => !t.IsTrivia).ToList();
            for (var i = 0; i + 1 < code.Count; i++)
            {
                if (code[i].Start >= caret) break;
                if (IsWord(code[i], "VAR") && code[i + 1].Kind == DaxLexKind.Identifier && code[i + 1].End <= caret)
                    names.Add(code[i + 1].Text);
                if (IsWord(code[i], "FUNCTION") && code[i + 1].Kind == DaxLexKind.Identifier && i + 2 < code.Count && code[i + 2].Text == "=")
                {
                    var start = i + 3 < code.Count ? code[i + 3].Start : code[i + 2].End;
                    var end = start;
                    var depth = 0;
                    for (var j = i + 3; j < code.Count; j++)
                    {
                        if (code[j].Text == "(") depth++;
                        else if (code[j].Text == ")" && --depth == 0) { end = j + 1 < code.Count ? code[j + 1].End : code[j].End; break; }
                    }
                    var source = string.Concat(code.Where(t => t.Start >= start && t.End <= end).Select(t => t.Text + " "));
                    var signature = DaxSignatureCatalog.ParseUserDefined(code[i + 1].Text, source);
                    if (signature != null)
                    {
                        udfs.Add(signature);
                        if (caret > start) names.AddRange(signature.Parameters.Select(p => p.Name));
                    }
                }
            }
            functions = udfs;
            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
