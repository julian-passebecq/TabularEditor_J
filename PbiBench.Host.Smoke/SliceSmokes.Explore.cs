using System;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using PbiBench.Core.Optimize;
using TabularEditor.BestPracticeAnalyzer;
using TabularEditor.PbiBench.Dax;
using TabularEditor.PbiBench.Explore;
using TabularEditor.PbiBench.Optimize;
using TabularEditor.TOMWrapper;

// S7: Explore / Optimize (EXP-002 profiling drafts, OPT-001 curated BPA + assertions).
internal static partial class SliceSmokes
{
    static partial void EarlyExplore(TabularModelHandler handler)
    {
        var model = handler.Model;
        var sales = model.AddTable("S7 Sales");
        var customer = model.AddTable("S7 Customer");
        SingleColumnRelationship rel = null;
        try
        {
            var custKey = sales.AddDataColumn("CustKey", "CustKey", null, DataType.Int64);
            var amount = sales.AddDataColumn("Amount", "Amount", null, DataType.Double);
            var dimKey = customer.AddDataColumn("CustKey", "CustKey", null, DataType.Int64);
            dimKey.IsHidden = true;
            customer.AddDataColumn("Name", "Name", null, DataType.String);
            var total = sales.AddMeasure("S7 Total", "SUM('S7 Sales'[Amount])");
            total.FormatString = "#,0"; total.Description = "Synthetic total";
            var ratio = sales.AddMeasure("S7 Ratio", "[S7 Total] / 2");
            ratio.Description = "No format string on purpose";
            var broken = sales.AddMeasure("S7 Broken", "SUM('S7 Gone'[x])");
            broken.FormatString = "0"; broken.Description = "Unresolved on purpose";
            rel = model.AddRelationship();
            rel.FromColumn = custKey; rel.ToColumn = dimKey;
            rel.CrossFilteringBehavior = CrossFilteringBehavior.BothDirections;
            rel.Name = "S7 Sales to Customer";

            ProfileDraftSmoke(model, amount, custKey, rel);
            ModelChecksSmoke(handler, ratio, broken, custKey, amount, rel);
        }
        finally
        {
            if (rel != null && !rel.IsRemoved) rel.Delete();
            if (!sales.IsRemoved) sales.Delete();
            if (!customer.IsRemoved) customer.Delete();
        }
    }

    private static void ExploreCheck(bool condition, string message) { if (!condition) throw new Exception("S7: " + message); }

    private static void ProfileDraftSmoke(Model model, DataColumn amount, DataColumn custKey, SingleColumnRelationship rel)
    {
        var columnDraft = PbiBenchProfileForm.CreateColumnDraft(amount, 5);
        ExploreCheck(columnDraft.Query.Contains("MIN('S7 Sales'[Amount])") && columnDraft.Query.Contains("TOPN(5,"), "Numeric column draft from TOM data type.");
        var coverage = PbiBenchProfileForm.CreateCoverageDraft(rel, 12);
        ExploreCheck(coverage.Query.Contains("EXCEPT(DISTINCT('S7 Sales'[CustKey]), DISTINCT('S7 Customer'[CustKey]))") && coverage.RowLimit == 12,
            "Coverage draft from many side to one side.");

        using (var form = new PbiBenchProfileForm(model))
        {
            var handle = form.Handle;
            ExploreLayout(form);
            form.Tables.SelectedItem = amount.Table;
            form.Columns.SelectedItem = amount;
            form.Limit.Value = 101;
            ExploreCheck(!form.TryCreate(out var tooMany) && tooMany.Contains("between 1 and 100"), "Top values > 100 refused.");
            form.Limit.Value = 7;
            ExploreCheck(form.TryCreate(out _) && form.RowLimit == 7 && form.Query.Contains("'S7 Sales'[Amount]") && form.IsCurrent(), "Column draft created.");
            var query = form.Query;
            amount.Name = "Amount renamed";
            ExploreCheck(!form.IsCurrent(), "Draft is stale after the profiled column is renamed.");
            amount.Name = "Amount";
            ExploreCheck(form.IsCurrent(), "Draft current again after rename back.");

            // Opening the draft never executes it.
            var executor = new CountingExecutor();
            using (var workbench = new PbiBenchDaxWorkbenchForm(executor, "Synthetic", ""))
            {
                var wbHandle = workbench.Handle;
                workbench.LoadPreviewDraft(query, form.RowLimit);
                workbench.ExecutionContextIsCurrent = () => form.IsCurrent();
                var editor = (Control)typeof(PbiBenchDaxWorkbenchForm).GetField("_queryEditor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(workbench);
                ExploreCheck(editor.Text == query, "Draft text loaded unchanged.");
                ExploreCheck(executor.Calls == 0, "Loading a profile draft must not execute it.");
                amount.Name = "Amount renamed";
                typeof(PbiBenchDaxWorkbenchForm).GetMethod("ExecuteQuery", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(workbench, null);
                Application.DoEvents();
                ExploreCheck(executor.Calls == 0, "Stale profile draft must not execute.");
                amount.Name = "Amount";
            }

            form.RelationshipMode.Checked = true;
            form.Limit.Value = 1000;
            ExploreCheck(form.TryCreate(out _) && form.RowLimit == 1000 && form.Query.Contains("TOPN(1000, EXCEPT("), "Relationship draft created.");
            custKey.Name = "CustKey2";
            ExploreCheck(!form.IsCurrent(), "Coverage draft stale after key rename.");
            custKey.Name = "CustKey";
        }
        Console.WriteLine("S7 profile drafts: TOM kinds, bounds, stale guard, opens unsaved without executing PASS");
    }

    private static void ModelChecksSmoke(TabularModelHandler handler, Measure ratio, Measure broken, DataColumn custKey, DataColumn amount,
        SingleColumnRelationship rel)
    {
        var model = handler.Model;
        var annotationsBefore = model.GetAnnotations().Count();
        var analyzer = new Analyzer();
        var userRulesBefore = analyzer.AllRules.Count();

        // Every curated rule compiles and evaluates on a real TOMWrapper model.
        foreach (var rule in PbiBenchCuratedRules.Create())
        {
            var results = rule.Analyze(model).ToList();
            ExploreCheck(results.All(r => !r.RuleHasError && !r.InvalidCompatibilityLevel),
                "Curated rule " + rule.ID + " failed: " + string.Join("; ", results.Where(r => r.RuleHasError).Select(r => r.RuleError)));
        }
        var effective = PbiBenchCuratedRules.Effective(analyzer, false);
        ExploreCheck(effective.Count == PbiBenchCuratedRules.Count && effective.All(r => r.ID.StartsWith(PbiBenchCuratedRules.IdPrefix)),
            "Curated-only run goes through GetEffectiveRules with exactly the curated pack.");

        var result = PbiBenchModelChecks.Run(model, analyzer, false, "");
        bool Has(string code, ITabularNamedObject target) => result.Rows.Any(r => r.Code == code && ReferenceEquals(r.Target, target));
        ExploreCheck(Has(SemanticAssertions.FormatString, ratio), "ASSERT-FMT finds the measure without a format string.");
        ExploreCheck(Has(SemanticAssertions.UnresolvedReferences, broken), "ASSERT-REF uses the S2 usage index.");
        ExploreCheck(Has(SemanticAssertions.Bidirectional, rel), "ASSERT-BIDI finds the both-direction relationship.");
        ExploreCheck(Has(SemanticAssertions.HiddenKeys, custKey), "ASSERT-KEY finds the visible many-side key.");
        ExploreCheck(!result.Rows.Any(r => r.Code == SemanticAssertions.MeasureDescription && r.ObjectPath.Contains("S7")), "S7 measures have descriptions.");
        ExploreCheck(Has(PbiBenchCuratedRules.IdPrefix + "DAX_DIVISION", ratio) && !Has(PbiBenchCuratedRules.IdPrefix + "DAX_DIVISION", broken),
            "Curated division rule flags '/' only.");
        ExploreCheck(Has(PbiBenchCuratedRules.IdPrefix + "FLOAT_COLUMN", amount), "Curated Double rule.");
        ExploreCheck(result.RuleErrors == 0 && result.RulesRun == PbiBenchCuratedRules.Count, "No curated rule errors.");

        var allowed = PbiBenchModelChecks.Run(model, analyzer, false, rel.Name.ToUpperInvariant());
        ExploreCheck(rel.Name.Length > 0, "Relationship has a name.");
        var byLabel = PbiBenchModelChecks.Run(model, analyzer, false, "'S7 Sales'[CustKey] -> 'S7 Customer'[CustKey]");
        ExploreCheck(!byLabel.Rows.Any(r => r.Code == SemanticAssertions.Bidirectional && ReferenceEquals(r.Target, rel)), "Allowlist by From -> To label.");
        ExploreCheck(!allowed.Rows.Any(r => r.Code == SemanticAssertions.Bidirectional && ReferenceEquals(r.Target, rel)), "Allowlist by relationship name: " + rel.Name);

        // Same-ID loaded rule overrides the curated one (TE2 precedence) without touching any collection file.
        if (analyzer.LocalUserRules != null)
        {
            var overrideRule = new BestPracticeRule { ID = PbiBenchCuratedRules.IdPrefix + "FLOAT_COLUMN", Name = "override", Scope = RuleScope.Model, Expression = "false" };
            var withUser = analyzer.GetEffectiveRules(false, false, false, false,
                PbiBenchCuratedRules.Create().Where(r => r.ID != overrideRule.ID).Concat(new[] { overrideRule }));
            ExploreCheck(withUser.Count(r => r.ID == overrideRule.ID) == 1, "One rule per ID.");
        }
        ExploreCheck(analyzer.AllRules.Count() == userRulesBefore && !analyzer.AllRules.Any(r => r.ID.StartsWith(PbiBenchCuratedRules.IdPrefix)),
            "Curated rules never join a rule collection.");
        ExploreCheck(model.GetAnnotations().Count() == annotationsBefore, "Model checks never write annotations.");

        using (var form = new PbiBenchAssertionsForm((allow, user) => PbiBenchModelChecks.Run(model, analyzer, user, allow), null, ""))
        {
            var handle = form.Handle;
            ExploreLayout(form);
            ITabularNamedObject navigated = null;
            var nav = new PbiBenchAssertionsForm((allow, user) => PbiBenchModelChecks.Run(model, analyzer, user, allow), o => navigated = o, "");
            using (nav)
            {
                var h2 = nav.Handle;
                nav.RunChecks();
                ExploreCheck(nav.RowCount == nav.Result.Rows.Count && nav.RowCount > 0, "Results list rows.");
                var row = nav.Result.Rows.ToList().FindIndex(r => r.Code == SemanticAssertions.UnresolvedReferences && ReferenceEquals(r.Target, broken));
                ExploreCheck(ReferenceEquals(nav.NavigateRow(row), broken) && ReferenceEquals(navigated, broken), "Goto navigation from a finding.");
                var removedRow = nav.Result.Rows.ToList().FindIndex(r => ReferenceEquals(r.Target, ratio));
                ratio.Delete();
                navigated = null;
                ExploreCheck(nav.NavigateRow(removedRow) == null && navigated == null, "Removed object is not navigated.");
            }
        }
        Console.WriteLine("S7 model checks: curated BPA via GetEffectiveRules, 5 assertions, allowlist, no writes, Goto PASS");
    }

    private static void ExploreLayout(Form form)
    {
        foreach (var size in new[] { form.MinimumSize, new Size(1400, 900) })
        {
            form.Size = size;
            form.PerformLayout();
        }
        form.Scale(new SizeF(1.5f, 1.5f));
        form.Size = form.MinimumSize;
        form.PerformLayout();
        foreach (Control c in form.Controls)
            ExploreCheck(c.Width > 0 && c.Height > 0, form.GetType().Name + ": control " + c.GetType().Name + " collapsed.");
    }

    private sealed class CountingExecutor : IDaxQueryExecutor
    {
        public int Calls;
        public Task<DaxQueryResult> ExecuteAsync(DaxQueryRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return Task.FromResult(new DaxQueryResult());
        }
    }
}
