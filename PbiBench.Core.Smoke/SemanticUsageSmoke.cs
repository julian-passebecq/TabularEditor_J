using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Navigation;
using PbiBench.Core.Semantic;

internal static class SemanticUsageSmoke
{
    public static void Run()
    {
        var objects = new[] {
            new SemanticUsageObject { Id = "t", Kind = SemanticObjectKind.Table, Name = "Sales", Path = "Sales" },
            new SemanticUsageObject { Id = "base", Kind = SemanticObjectKind.Measure, Name = "Total Sales", Path = "Sales / Total Sales" },
            new SemanticUsageObject { Id = "margin", Kind = SemanticObjectKind.Measure, Name = "Margin %", Path = "Sales / Margin %" },
            new SemanticUsageObject { Id = "orphan", Kind = SemanticObjectKind.Measure, Name = "Orphan", Path = "Sales / Orphan", IsHidden = true },
            new SemanticUsageObject { Id = "visual", Kind = SemanticObjectKind.Measure, Name = "Visual only", Path = "Sales / Visual only" },
            new SemanticUsageObject { Id = "item", Kind = SemanticObjectKind.CalculationItem, Name = "YTD", Path = "Time / YTD" }
        };
        var graph = new SemanticDependencyGraph(new[] {
            new SemanticDependencyEdge { SourceId = "margin", TargetId = "base" },
            new SemanticDependencyEdge { SourceId = "item", TargetId = "base" },
            new SemanticDependencyEdge { SourceId = "margin", TargetId = "margin" } // self edge must not count as use
        });
        var names = SliceSmokes.SyntheticNames();
        var unresolved = new List<SemanticUnresolvedReference>();
        unresolved.AddRange(SemanticUsageIndex.FindUnresolved("margin", "Sales / Margin %", "Expression", "DIVIDE([Total Sales], [Deleted Cost]) + Sales[Nope] + BLANK(1)", names));
        unresolved.AddRange(SemanticUsageIndex.FindUnresolved("item", "Time / YTD", "Expression", "CALCULATE(SELECTEDMEASURE(), DATESYTD('Calendar'[Date]))", names));
        unresolved.AddRange(SemanticUsageIndex.FindUnresolved("base", "Sales / Total Sales", "Expression", "SUM(Sales[Amount])", names));
        SliceSmokes.Check(unresolved.Count == 3, "Unresolved count: " + string.Join(" | ", unresolved.Select(u => u.Code + " " + u.Reference)));
        SliceSmokes.Check(unresolved.Any(u => u.Reference == "[Deleted Cost]") && unresolved.Any(u => u.Reference == "Sales[Nope]") && unresolved.Any(u => u.Reference == "Calendar"), "Unresolved references found.");
        SliceSmokes.Check(!unresolved.Any(u => u.Code == PbiBench.Core.Dax.DaxDiagnosticCodes.Arity), "Arity is not an unresolved reference.");
        SliceSmokes.Check(SemanticUsageIndex.FindUnresolved("x", "x", "Expression", "[Anything]", PbiBench.Core.Dax.DaxModelNameSet.Empty).Count == 0, "No model, no unresolved claims.");

        var index = SemanticUsageIndex.Build(objects, graph, unresolved, new Dictionary<string, int> { { "visual", 2 } });
        var baseUsage = index.Measures.Single(m => m.Measure.Id == "base");
        SliceSmokes.Check(baseUsage.Referrers.Select(r => r.Id).SequenceEqual(new[] { "margin", "item" }.OrderBy(id => objects.First(o => o.Id == id).Path, StringComparer.OrdinalIgnoreCase)), "Measure referrers.");
        SliceSmokes.Check(index.Measures.Single(m => m.Measure.Id == "margin").IsUnusedInModel, "Self reference is not a use.");
        var unused = index.UnusedMeasures.Select(m => m.Measure.Id).OrderBy(x => x).ToArray();
        SliceSmokes.Check(unused.SequenceEqual(new[] { "margin", "orphan" }), "Unused measures: " + string.Join(",", unused));
        SliceSmokes.Check(index.Measures.Single(m => m.Measure.Id == "visual").ExternalReferences == 2 && index.Measures.Single(m => m.Measure.Id == "orphan").ExternalReferences == null, "External usage is optional and explicit.");
        SliceSmokes.Check(index.Unresolved.First().SourcePath == "Sales / Margin %" && !index.UnresolvedTruncated, "Unresolved ordering.");
        SliceSmokes.Check(index.GetReferrers("base").Count == 2 && index.GetReferrers("missing").Count == 0, "Referrer lookup.");
        Console.WriteLine("Semantic usage: referrers, unused measures, unresolved references, external counts PASS");
    }
}
