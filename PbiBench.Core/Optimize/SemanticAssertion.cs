using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Semantic;

namespace PbiBench.Core.Optimize
{
    /// <summary>Neutral measure facts supplied by the host (no TOM types).</summary>
    public sealed class AssertionMeasure
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsHidden { get; set; }
        public string FormatString { get; set; } = "";
        public string FormatStringExpression { get; set; } = "";
        public string Description { get; set; } = "";
    }

    public sealed class AssertionColumn
    {
        public string Id { get; set; } = "";
        public string Path { get; set; } = "";
        /// <summary>True when the column or its table is hidden.</summary>
        public bool IsHidden { get; set; }
    }

    public sealed class AssertionRelationship
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>Many-side column id (FromColumn in TOM); empty when unresolved.</summary>
        public string FromColumnId { get; set; } = "";
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public bool BothDirections { get; set; }
        public bool Active { get; set; }
    }

    public sealed class AssertionInput
    {
        public IReadOnlyList<AssertionMeasure> Measures { get; set; } = Array.Empty<AssertionMeasure>();
        public IReadOnlyList<AssertionColumn> Columns { get; set; } = Array.Empty<AssertionColumn>();
        public IReadOnlyList<AssertionRelationship> Relationships { get; set; } = Array.Empty<AssertionRelationship>();
        /// <summary>From <see cref="SemanticUsageIndex.Unresolved"/> (S2), so the same advisory rules apply.</summary>
        public IReadOnlyList<SemanticUnresolvedReference> Unresolved { get; set; } = Array.Empty<SemanticUnresolvedReference>();
        public bool UnresolvedTruncated { get; set; }
        /// <summary>Relationship names or "From -> To" / "From → To" labels allowed to filter both directions (case-insensitive).</summary>
        public IReadOnlyCollection<string> BidirectionalAllowlist { get; set; } = Array.Empty<string>();
    }

    public sealed class AssertionDefinition
    {
        public AssertionDefinition(string id, string title, string rationale) { Id = id; Title = title; Rationale = rationale; }
        public string Id { get; }
        public string Title { get; }
        public string Rationale { get; }
    }

    public sealed class AssertionFinding
    {
        public string AssertionId { get; set; } = "";
        public string ObjectId { get; set; } = "";
        public string ObjectPath { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public sealed class AssertionOutcome
    {
        public AssertionDefinition Definition { get; set; } = SemanticAssertions.All[0];
        public int Checked { get; set; }
        public int Failed { get; set; }
        public bool Passed => Failed == 0;
    }

    public sealed class AssertionReport
    {
        public IReadOnlyList<AssertionOutcome> Outcomes { get; set; } = Array.Empty<AssertionOutcome>();
        public IReadOnlyList<AssertionFinding> Findings { get; set; } = Array.Empty<AssertionFinding>();
        public bool Truncated { get; set; }
        public bool Passed => Outcomes.All(o => o.Passed);
        public string Summary => Outcomes.Count(o => o.Passed) + "/" + Outcomes.Count + " assertions pass · " + Findings.Count +
            (Truncated ? "+" : "") + " finding(s)";
    }

    /// <summary>
    /// OPT-001 semantic assertions: read-only checks over neutral model facts. Nothing is fixed or
    /// written; the host navigates to the object owning each finding.
    /// </summary>
    public static class SemanticAssertions
    {
        public const int MaxFindings = 5000;
        public const int MaxAllowlistEntries = 256;
        public const string FormatString = "ASSERT-FMT";
        public const string UnresolvedReferences = "ASSERT-REF";
        public const string Bidirectional = "ASSERT-BIDI";
        public const string HiddenKeys = "ASSERT-KEY";
        public const string MeasureDescription = "ASSERT-DESC";

        public static readonly IReadOnlyList<AssertionDefinition> All = new[] {
            new AssertionDefinition(FormatString, "Visible measures have a format string",
                "A static format string or a format string expression keeps report formatting consistent."),
            new AssertionDefinition(UnresolvedReferences, "No unresolved DAX references",
                "Advisory lexical check (S2): unknown table, column/measure or identifier in model DAX."),
            new AssertionDefinition(Bidirectional, "No bi-directional relationships outside the allowlist",
                "Both-direction filtering can create ambiguity and slow queries; allow intended ones by name."),
            new AssertionDefinition(HiddenKeys, "Many-side relationship key columns are hidden",
                "Report authors should filter through the one-side column, not the foreign key."),
            new AssertionDefinition(MeasureDescription, "Visible measures have a description",
                "Descriptions show as tooltips in report field lists.")
        };

        public static IReadOnlyCollection<string> ParseAllowlist(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            return text!.Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()).Where(s => s.Length > 0 && s.Length <= 1024)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxAllowlistEntries).ToArray();
        }

        public static AssertionReport Evaluate(AssertionInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var findings = new List<AssertionFinding>();
            var truncated = false;
            var outcomes = new List<AssertionOutcome>();
            void Add(AssertionOutcome outcome, string id, string path, string message)
            {
                outcome.Failed++;
                if (findings.Count >= MaxFindings) { truncated = true; return; }
                findings.Add(new AssertionFinding { AssertionId = outcome.Definition.Id, ObjectId = id ?? "", ObjectPath = path ?? "", Message = message });
            }
            AssertionOutcome Start(string id)
            {
                var o = new AssertionOutcome { Definition = All.First(d => d.Id == id) };
                outcomes.Add(o);
                return o;
            }

            var measures = (input.Measures ?? Array.Empty<AssertionMeasure>()).Where(m => m != null).ToArray();
            var visible = measures.Where(m => !m.IsHidden).ToArray();

            var fmt = Start(FormatString);
            fmt.Checked = visible.Length;
            foreach (var m in visible)
                if (string.IsNullOrWhiteSpace(m.FormatString) && string.IsNullOrWhiteSpace(m.FormatStringExpression))
                    Add(fmt, m.Id, m.Path, "Visible measure has no format string.");

            var refs = Start(UnresolvedReferences);
            var unresolved = (input.Unresolved ?? Array.Empty<SemanticUnresolvedReference>()).Where(u => u != null).ToArray();
            refs.Checked = unresolved.Select(u => u.SourceId).Distinct(StringComparer.Ordinal).Count();
            foreach (var u in unresolved)
                Add(refs, u.SourceId, u.SourcePath, u.Property + ": " + u.Message);
            if (input.UnresolvedTruncated) truncated = true;

            var bidi = Start(Bidirectional);
            var allow = new HashSet<string>(input.BidirectionalAllowlist ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var relationships = (input.Relationships ?? Array.Empty<AssertionRelationship>()).Where(r => r != null).ToArray();
            bidi.Checked = relationships.Length;
            foreach (var r in relationships.Where(r => r.BothDirections))
            {
                if ((r.Name.Length > 0 && allow.Contains(r.Name)) || allow.Contains(r.From + " -> " + r.To) || allow.Contains(r.From + " → " + r.To)) continue;
                Add(bidi, r.Id, r.From + " → " + r.To, "Filters both directions and is not in the allowlist" + (r.Active ? "." : " (inactive)."));
            }

            var keys = Start(HiddenKeys);
            var columns = (input.Columns ?? Array.Empty<AssertionColumn>()).Where(c => c != null && c.Id.Length > 0)
                .GroupBy(c => c.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var keyIds = relationships.Select(r => r.FromColumnId).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
            keys.Checked = keyIds.Length;
            foreach (var id in keyIds)
                if (columns.TryGetValue(id, out var column) && !column.IsHidden)
                    Add(keys, column.Id, column.Path, "Many-side key column is visible.");

            var desc = Start(MeasureDescription);
            desc.Checked = visible.Length;
            foreach (var m in visible)
                if (string.IsNullOrWhiteSpace(m.Description))
                    Add(desc, m.Id, m.Path, "Visible measure has no description.");

            return new AssertionReport { Outcomes = outcomes, Findings = findings, Truncated = truncated };
        }
    }
}
