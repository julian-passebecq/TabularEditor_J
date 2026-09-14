using System;
using System.Collections.Generic;
using System.Linq;

namespace PbiBench.Core.Dax
{
    // Tokens come from the host's existing TE2 lexer. This is structural assistance,
    // not a second grammar, binder, or engine validator.
    public sealed class DaxAuthoringToken
    {
        public string Text { get; set; } = "";
        public int Start { get; set; }
        public int Length { get; set; }
        public bool IsTriviaOrLiteral { get; set; }
    }

    public sealed class DaxAuthoringIssue
    {
        public int Start { get; set; }
        public int Length { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class DaxAuthoringItem
    {
        public string Name { get; set; } = "";
        public string InsertText { get; set; } = "";
        public string Kind { get; set; } = "";
        public string Detail { get; set; } = "";
        public string ObjectId { get; set; } = "";
    }

    public static class DaxAuthoring
    {
        // Small curated local help set. Unlisted/engine-specific functions still get
        // argument position; absence never implies that a function is invalid.
        private static readonly Dictionary<string, string> Signatures = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "SUM", "SUM(column)" }, { "SUMX", "SUMX(table, expression)" },
            { "FILTER", "FILTER(table, filter)" }, { "CALCULATE", "CALCULATE(expression, filter…)" },
            { "DIVIDE", "DIVIDE(numerator, denominator, alternateResult?)" },
            { "IF", "IF(test, trueValue, falseValue?)" }, { "ROW", "ROW(name, expression, …)" },
            { "TOPN", "TOPN(count, table, orderByExpression?, order?, …)" }
        };
        public const int MaxTextLength = 256 * 1024;
        public const int MaxSuggestions = 200;

        public static IReadOnlyList<DaxAuthoringItem> Search(IEnumerable<DaxAuthoringItem> items, string filter)
        {
            filter = (filter ?? "").Trim();
            return items.Where(x => x.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                x.InsertText.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderByDescending(x => x.Name.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
                .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.InsertText, StringComparer.OrdinalIgnoreCase).Take(MaxSuggestions).ToArray();
        }

        public static IReadOnlyList<DaxAuthoringIssue> CheckStructure(IEnumerable<DaxAuthoringToken> tokens)
        {
            var stack = new Stack<DaxAuthoringToken>();
            var issues = new List<DaxAuthoringIssue>();
            foreach (var token in tokens)
            {
                if (token.IsTriviaOrLiteral) continue;
                if (token.Text == "(" || token.Text == "{") stack.Push(token);
                else if (token.Text == ")" || token.Text == "}")
                {
                    if (stack.Count == 0 || stack.Peek().Text != (token.Text == ")" ? "(" : "{"))
                        issues.Add(new DaxAuthoringIssue { Start = token.Start, Length = token.Length, Message = "Unmatched closing delimiter." });
                    else stack.Pop();
                }
                if (issues.Count >= 100) break;
            }
            foreach (var token in stack.Take(Math.Max(0, 100 - issues.Count)))
                issues.Add(new DaxAuthoringIssue { Start = token.Start, Length = token.Length, Message = "Unclosed delimiter." });
            return issues.OrderBy(x => x.Start).ToArray();
        }

        public static string ParameterHelp(IEnumerable<DaxAuthoringToken> tokens, int caret)
        {
            var frames = new Stack<Tuple<string, int>>();
            var previous = "";
            foreach (var token in tokens)
            {
                if (token.Start >= caret) break;
                if (token.IsTriviaOrLiteral) continue;
                if (token.Text == "(" || token.Text == "{") frames.Push(Tuple.Create(token.Text == "(" ? previous : "", 1));
                else if (token.Text == ")" || token.Text == "}") { if (frames.Count > 0) frames.Pop(); }
                else if ((token.Text == "," || token.Text == ";") && frames.Count > 0)
                {
                    var frame = frames.Pop();
                    frames.Push(Tuple.Create(frame.Item1, frame.Item2 + 1));
                }
                previous = token.Text;
            }
            foreach (var frame in frames)
                if (frame.Item1.Length > 0 && (char.IsLetter(frame.Item1[0]) || frame.Item1[0] == '_'))
                    return (Signatures.TryGetValue(frame.Item1, out var signature) ? signature : frame.Item1) + " · argument " + frame.Item2;
            return "No enclosing function call.";
        }
    }
}
