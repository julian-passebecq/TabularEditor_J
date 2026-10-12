using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Optimize;
using TabularEditor.BestPracticeAnalyzer;
using TabularEditor.PbiBench.Semantic;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Optimize
{
    internal sealed class PbiBenchCheckRow
    {
        public string Group { get; set; } = "";
        public string Code { get; set; } = "";
        public string Title { get; set; } = "";
        public string ObjectPath { get; set; } = "";
        public string Message { get; set; } = "";
        public int Severity { get; set; }
        /// <summary>Live object for TE2 Goto; null for rule errors or model-level rows that cannot be navigated.</summary>
        public ITabularNamedObject Target { get; set; }
    }

    internal sealed class PbiBenchCheckResult
    {
        public AssertionReport Assertions { get; set; }
        public int CuratedRuleCount { get; set; }
        public int RulesRun { get; set; }
        public int RuleErrors { get; set; }
        public int BpaViolations { get; set; }
        public bool Truncated { get; set; }
        public IReadOnlyList<PbiBenchCheckRow> Rows { get; set; } = Array.Empty<PbiBenchCheckRow>();
        public string Summary => Assertions.Summary + " · BPA " + RulesRun + " rule(s), " + BpaViolations + " violation(s)" +
            (RuleErrors > 0 ? ", " + RuleErrors + " rule error(s)" : "") + (Truncated ? " · list truncated" : "");
    }

    /// <summary>
    /// Read-only model checks: OPT-001 semantic assertions over a fresh <see cref="PbiBenchSemanticSnapshot"/>
    /// (unresolved references come from the S2 usage index) plus the curated BPA pack run through TE2's
    /// rule engine. Never writes the model, annotations or rule files.
    /// </summary>
    internal static class PbiBenchModelChecks
    {
        internal const int MaxRows = 5000;
        /// <summary>Optional model annotation (read only) holding a newline-separated allowlist.</summary>
        internal const string AllowlistAnnotation = "TE2Enhanced_BidirectionalAllowlist";

        internal static string ReadAllowlistAnnotation(Model model)
        {
            try { return model?.GetAnnotation(AllowlistAnnotation) ?? ""; }
            catch (Exception) { return ""; }
        }

        internal static AssertionInput BuildInput(Model model, PbiBenchSemanticSnapshot snapshot, string allowlist)
        {
            var index = snapshot.Index;
            string Id(ITabularNamedObject obj) => obj != null && index.TryGetId(obj, out var id) ? id : "";
            var measures = model.AllMeasures.Select(m => new AssertionMeasure {
                Id = Id(m), Path = m.DaxObjectFullName, IsHidden = m.IsHidden, FormatString = m.FormatString ?? "",
                FormatStringExpression = m.FormatStringExpression ?? "", Description = m.Description ?? ""
            }).ToArray();
            var columns = model.AllColumns.Select(c => new AssertionColumn {
                Id = Id(c), Path = c.DaxObjectFullName, IsHidden = !c.IsVisible
            }).ToArray();
            var relationships = model.Relationships.OfType<SingleColumnRelationship>().Select(r => new AssertionRelationship {
                Id = Id(r), Name = r.Name ?? "",
                FromColumnId = r.FromColumn == null || r.FromColumn.IsRemoved ? "" : Id(r.FromColumn),
                From = r.FromColumn?.DaxObjectFullName ?? "Unresolved source", To = r.ToColumn?.DaxObjectFullName ?? "Unresolved target",
                BothDirections = r.CrossFilteringBehavior == CrossFilteringBehavior.BothDirections, Active = r.IsActive
            }).ToArray();
            return new AssertionInput {
                Measures = measures, Columns = columns, Relationships = relationships,
                Unresolved = snapshot.Usage.Unresolved, UnresolvedTruncated = snapshot.Usage.UnresolvedTruncated,
                BidirectionalAllowlist = SemanticAssertions.ParseAllowlist(allowlist)
            };
        }

        internal static PbiBenchCheckResult Run(Model model, Analyzer analyzer, bool includeUserRules, string allowlist)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var snapshot = PbiBenchSemanticSnapshot.Build(model);
            var report = SemanticAssertions.Evaluate(BuildInput(model, snapshot, allowlist));
            var rows = new List<PbiBenchCheckRow>();
            var titles = SemanticAssertions.All.ToDictionary(d => d.Id, d => d.Title);
            foreach (var f in report.Findings)
            {
                if (rows.Count >= MaxRows) break;
                var target = snapshot.Index.Resolve(f.ObjectId);
                rows.Add(new PbiBenchCheckRow { Group = "Assertion", Code = f.AssertionId, Title = titles[f.AssertionId],
                    ObjectPath = f.ObjectPath, Message = f.Message, Severity = 2, Target = target == null || target.IsRemoved ? null : target });
            }

            var rules = PbiBenchCuratedRules.Effective(analyzer, includeUserRules);
            int errors = 0, violations = 0;
            var truncated = report.Truncated;
            foreach (var rule in rules)
            {
                foreach (var r in rule.Analyze(model))
                {
                    if (!r.RuleEnabled) break;
                    if (r.RuleHasError || r.InvalidCompatibilityLevel)
                    {
                        errors++;
                        if (rows.Count < MaxRows) rows.Add(new PbiBenchCheckRow { Group = "BPA", Code = rule.ID, Title = rule.Name, Severity = 3,
                            ObjectPath = "(rule)", Message = r.InvalidCompatibilityLevel ? "Rule needs a higher compatibility level." : "Rule error: " + r.RuleError });
                        continue;
                    }
                    if (r.Object == null || r.Ignored) continue;
                    violations++;
                    if (rows.Count >= MaxRows) { truncated = true; continue; }
                    rows.Add(new PbiBenchCheckRow { Group = "BPA", Code = rule.ID, Title = rule.Name, Severity = rule.Severity,
                        ObjectPath = r.Object is IDaxObject dax ? dax.DaxObjectFullName : r.ObjectName, Message = rule.Description ?? "",
                        Target = r.Object.IsRemoved ? null : r.Object });
                }
            }
            if (report.Findings.Count > MaxRows) truncated = true;
            return new PbiBenchCheckResult {
                Assertions = report, CuratedRuleCount = PbiBenchCuratedRules.Count, RulesRun = rules.Count,
                RuleErrors = errors, BpaViolations = violations, Truncated = truncated, Rows = rows
            };
        }
    }
}
