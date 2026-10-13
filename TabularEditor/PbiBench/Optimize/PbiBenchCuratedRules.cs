using System.Collections.Generic;
using System.Linq;
using TabularEditor.BestPracticeAnalyzer;

namespace TabularEditor.PbiBench.Optimize
{
    /// <summary>
    /// OPT-001 curated BPA pack, kept as C# constants (no embedded resource, no rule file). Rules are
    /// handed to TE2's <see cref="Analyzer.GetEffectiveRules"/> as <c>additionalRules</c>, so a user,
    /// machine, external or model rule with the same ID takes precedence. Nothing here writes a rule
    /// file, a model annotation or an ignore list. Expressions are TE2 Dynamic LINQ written fresh for
    /// this fork; every rule is compile-checked by the host smoke.
    /// </summary>
    internal static class PbiBenchCuratedRules
    {
        internal const string Category = "TE2 Enhanced curated";
        internal const string IdPrefix = "TE2E_";

        private sealed class Definition
        {
            public string Id, Name, Description, Expression;
            public RuleScope Scope;
            public int Severity;
        }

        private const RuleScope DaxScopes = RuleScope.Measure | RuleScope.CalculatedColumn | RuleScope.CalculationItem;
        private const RuleScope ColumnScopes = RuleScope.DataColumn | RuleScope.CalculatedColumn | RuleScope.CalculatedTableColumn;

        private static readonly Definition[] Definitions = {
            new Definition { Id = IdPrefix + "DAX_DIVISION", Severity = 2, Scope = DaxScopes,
                Name = "[TE2E] Use DIVIDE instead of '/' after a reference or call",
                Description = "'/' raises an error or infinity on zero; DIVIDE returns BLANK or an alternate result. Lexical heuristic: comments and strings are not excluded.",
                Expression = @"Regex.IsMatch(Expression, ""[\])]\s*/(?![/*])"")" },
            new Definition { Id = IdPrefix + "DAX_IFERROR", Severity = 2, Scope = DaxScopes,
                Name = "[TE2E] Avoid IFERROR / ISERROR",
                Description = "Error trapping forces cell-by-cell evaluation; test the condition (DIVIDE, ISBLANK) instead.",
                Expression = @"Regex.IsMatch(Expression, ""(?i)\b(IFERROR|ISERROR)\s*\("")" },
            new Definition { Id = IdPrefix + "FLOAT_COLUMN", Severity = 1, Scope = ColumnScopes,
                Name = "[TE2E] Prefer Decimal or Int64 over Double",
                Description = "Floating point columns can produce rounding differences in totals.",
                Expression = @"DataType == ""Double""" },
            new Definition { Id = IdPrefix + "NUMERIC_SUMMARIZE", Severity = 1, Scope = ColumnScopes,
                Name = "[TE2E] Visible numeric columns should not summarize by default",
                Description = "Implicit sums of keys or rates mislead report authors; expose explicit measures and set Summarize By to None.",
                Expression = @"IsVisible and SummarizeBy <> ""None"" and (DataType == ""Int64"" or DataType == ""Decimal"" or DataType == ""Double"")" },
            new Definition { Id = IdPrefix + "NAME_WHITESPACE", Severity = 2,
                Scope = RuleScope.Table | RuleScope.Measure | RuleScope.DataColumn | RuleScope.CalculatedColumn | RuleScope.Hierarchy,
                Name = "[TE2E] Object names have no leading or trailing spaces",
                Description = "Leading/trailing spaces are invisible in field lists and break DAX references typed by hand.",
                Expression = @"Name <> Name.Trim()" },
            new Definition { Id = IdPrefix + "DATE_TABLE", Severity = 1, Scope = RuleScope.Model,
                Name = "[TE2E] Model has a table marked as date table",
                Description = "Time intelligence functions need a contiguous date table marked as such (Data Category = Time).",
                Expression = @"not Tables.Any(DataCategory == ""Time"")" },
            new Definition { Id = IdPrefix + "MEASURE_FOLDER", Severity = 1, Scope = RuleScope.Measure,
                Name = "[TE2E] Organize visible measures in display folders (tables with more than 10)",
                Description = "Large flat measure lists are hard to browse in report field lists.",
                Expression = @"IsVisible and string.IsNullOrWhiteSpace(DisplayFolder) and Table.Measures.Count(IsVisible) > 10" }
        };

        internal static int Count => Definitions.Length;

        /// <summary>Fresh rule instances per run: TE2 caches compiled queries per rule instance and model.</summary>
        internal static IReadOnlyList<BestPracticeRule> Create() => Definitions.Select(d => new BestPracticeRule {
            ID = d.Id, Name = d.Name, Category = Category, Description = d.Description, Severity = d.Severity,
            Scope = d.Scope, Expression = d.Expression, CompatibilityLevel = 0
        }).ToArray();

        /// <summary>
        /// Rules to run. Curated only, or curated plus the analyzer's loaded rule collections; TE2's
        /// precedence applies (an existing rule with the same ID replaces the curated one).
        /// </summary>
        internal static IReadOnlyList<BestPracticeRule> Effective(Analyzer analyzer, bool includeUserRules)
        {
            var curated = Create();
            if (analyzer == null) return curated;
            // Loaded rules are copied so this read-only run never shares compiled queries or state with
            // TE2's own (possibly background) BPA analysis; the copies are never saved anywhere.
            return analyzer.GetEffectiveRules(includeUserRules, includeUserRules, includeUserRules, includeUserRules, curated)
                .Select(r => curated.Contains(r) ? r : Copy(r)).ToArray();
        }

        private static BestPracticeRule Copy(BestPracticeRule rule)
        {
            var copy = new BestPracticeRule();
            copy.AssignFrom(rule);
            return copy;
        }
    }
}
