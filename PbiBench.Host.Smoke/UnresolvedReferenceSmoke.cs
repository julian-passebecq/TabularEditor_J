using System;
using System.Linq;
using TabularEditor.PbiBench.Semantic;
using TabularEditor.TOMWrapper;

/// <summary>S2 MODEL-002 host checks: usage index from the live model, unresolved after delete, Usage tab navigation.</summary>
internal static class UnresolvedReferenceSmoke
{
    private static void Check(bool condition, string message) => SliceSmokes.DaxCheck(condition, message);

    internal static void Run(TabularModelHandler handler)
    {
        var model = handler.Model;
        var table = model.AddTable("S2 Usage");
        try
        {
            var baseMeasure = table.AddMeasure("S2 Base", "1");
            var dependent = table.AddMeasure("S2 Dependent", "[S2 Base] * 2");
            var orphan = table.AddMeasure("S2 Orphan", "SUM('S2 Missing'[x]) + VALUES('S2 Usage'[Nope])");

            var snapshot = PbiBenchSemanticSnapshot.Build(model);
            snapshot.Index.TryGetId(baseMeasure, out var baseId);
            snapshot.Index.TryGetId(dependent, out var dependentId);
            snapshot.Index.TryGetId(orphan, out var orphanId);
            var usage = snapshot.Usage;
            var baseUsage = usage.Measures.Single(m => m.Measure.Id == baseId);
            Check(baseUsage.Referrers.Any(r => r.Id == dependentId) && !baseUsage.IsUnusedInModel, "Measure referrers from TE2 dependencies.");
            Check(usage.UnusedMeasures.Any(m => m.Measure.Id == orphanId) && !usage.UnusedMeasures.Any(m => m.Measure.Id == baseId), "Unused-in-model measures.");
            Check(usage.Unresolved.Any(u => u.SourceId == orphanId && u.Reference == "S2 Missing"), "Unknown table reference.");
            Check(usage.Unresolved.Any(u => u.SourceId == orphanId && u.Reference == "S2 Usage[Nope]"), "Unknown column reference.");
            Check(!usage.Unresolved.Any(u => u.SourceId == dependentId || u.SourceId == baseId), "Resolved expressions must not be reported.");
            Check(usage.Unresolved.All(u => u.Property.Length > 0 && u.SourcePath.Length > 0), "Unresolved rows carry location.");

            baseMeasure.Delete();
            var after = PbiBenchSemanticSnapshot.Build(model);
            after.Index.TryGetId(dependent, out var dependentAfterId);
            Check(after.Usage.Unresolved.Any(u => u.SourceId == dependentAfterId && u.Reference == "[S2 Base]"), "Reference to a deleted measure must become unresolved.");
            Check(after.Names.Revision != snapshot.Names.Revision, "Name-set revision must change after delete.");

            using (var form = new PbiBenchSemanticViewForm(after))
            {
                var handle = form.Handle;
                form.SelectUsageTab();
                Check(form.UnresolvedRowCount == after.Usage.Unresolved.Count && form.UnusedMeasureRowCount == after.Usage.UnusedMeasures.Count(), "Usage tab rows.");
                var row = after.Usage.Unresolved.ToList().FindIndex(u => u.SourceId == dependentAfterId);
                var navigated = form.NavigateUsageRow(true, row);
                Check(navigated == dependentAfterId && after.Index.Resolve(navigated) == dependent, "Usage tab navigates to the expression owner.");
            }
            using (var form = new PbiBenchSemanticViewForm(after))
            {
                var handle = form.Handle;
                after.Index.TryGetId(orphan, out var orphanAfterId);
                var row = after.Usage.UnusedMeasures.ToList().FindIndex(m => m.Measure.Id == orphanAfterId);
                Check(form.NavigateUsageRow(false, row) == orphanAfterId, "Usage tab navigates to an unused measure.");
            }
        }
        finally
        {
            if (!table.IsRemoved) table.Delete();
        }
        Console.WriteLine("Unresolved references: referrers, unused measures, unknown table/column, delete → unresolved, Usage navigation PASS");
    }
}
