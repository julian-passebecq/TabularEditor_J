using System;
using System.Linq;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;
using TabularEditor.PbiBench.Navigation;
using TabularEditor.TOMWrapper;

internal static class AuthoringSmoke
{
    internal static void Run(Model model)
    {
        foreach (var valid in new[] {
            "EVALUATE ROW(\"(ignored)\", 1)",
            "VAR x = {1, 2} RETURN SUMX(x, [Value])",
            "/* ( outer /* nested } */ ) */ EVALUATE 'O''Brien'",
            "DEFINE FUNCTION F = (x: scalar val) => x + 1 EVALUATE ROW(\"v\", F(1))",
            "EVALUATE ROW(\"v\", 'Odd table'[Closing]]name]) -- ("
        })
            Check(DaxAuthoring.CheckStructure(PbiBenchDaxAuthoring.Tokens(valid)).Count == 0, "Valid corpus delimiter false positive.");
        foreach (var invalid in new[] { "EVALUATE ROW(\"v\", 1", "EVALUATE {1, 2)", "EVALUATE ROW(\"v\", 1))" })
            Check(DaxAuthoring.CheckStructure(PbiBenchDaxAuthoring.Tokens(invalid)).Count > 0, "Invalid corpus delimiter not found.");
        var help = "SUMX({1, 2}, IF(TRUE(), 1, 0)";
        Check(DaxAuthoring.ParameterHelp(PbiBenchDaxAuthoring.Tokens(help), help.Length).Contains("argument 2"), "Nested parameter counting.");
        var table = model.AddTable("Authoring O'Brien");
        var measure = table.AddMeasure("Amount]quoted", "1");
        var items = PbiBenchDaxAuthoring.Items(PbiBenchQuickOpenIndex.Build(model));
        Check(items.Any(x => x.InsertText == measure.DaxObjectFullName), "Exact TE2 quoting lost.");
        Check(items.Any(x => x.InsertText == "SUM("), "Upstream function missing.");
        measure.Name = "Renamed";
        items = PbiBenchDaxAuthoring.Items(PbiBenchQuickOpenIndex.Build(model));
        Check(!items.Any(x => x.Name == "Amount]quoted") && items.Any(x => x.Name == "Renamed"), "Stale rename.");
        measure.Delete();
        Check(!PbiBenchDaxAuthoring.Items(PbiBenchQuickOpenIndex.Build(model)).Any(x => x.Name == "Renamed"), "Stale delete.");
        table.Delete();
        var relationship = model.AddRelationship();
        var snapshot = TabularEditor.PbiBench.Semantic.PbiBenchSemanticSnapshot.Build(model);
        Check(snapshot.Relationships.Any(x => x.Unresolved), "Incomplete relationship must remain visible.");
        relationship.Delete();
        Console.WriteLine("Local DAX authoring: corpus, nested arguments, exact identifiers, rename/delete PASS");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
