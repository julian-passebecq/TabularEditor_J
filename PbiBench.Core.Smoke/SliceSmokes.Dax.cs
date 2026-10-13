using System;
using PbiBench.Core.Dax;

// S2: DAX language service and model usage (DAX-010/011, MODEL-002).
internal static partial class SliceSmokes
{
    static partial void Dax(string fixtureRoot)
    {
        DaxSignatureCatalogSmoke.Run();
        DaxCompletionContextSmoke.Run();
        DaxDiagnosticsCorpusSmoke.Run();
        SemanticUsageSmoke.Run();
        QaFixSmokes.Dax();
        // Oversized text: one DAX000 "skipped" advisory instead of an exception.
        var huge = DaxDiagnostics.Analyze("EVALUATE ROW(\"x\", 1)" + new string(' ', DaxAuthoring.MaxTextLength));
        if (huge.Count != 1 || huge[0].Code != "DAX000") throw new Exception("Oversized DAX must yield DAX000 only.");
        Console.WriteLine("DAX diagnostics size cap: DAX000 advisory, no exception PASS");
    }

    /// <summary>Synthetic model shared by the S2 Core smokes.</summary>
    internal static DaxModelNameSet SyntheticNames(params string[] vocabulary) => DaxModelNameSet.Create(new[] {
        new DaxTableNames { Name = "Sales", Columns = new[] { "Amount", "Quantity", "Order]Date" }, Measures = new[] { "Total Sales", "Margin %" } },
        new DaxTableNames { Name = "O'Brien Accounts", Columns = new[] { "Account", "Amount" }, Measures = new[] { "Balance" } },
        new DaxTableNames { Name = "Date", Columns = new[] { "Date", "Year" } }
    }, new[] { DaxSignatureCatalog.ParseUserDefined("Lib.AddOne", "(x : SCALAR VAL) => x + 1")! }, vocabulary);

    internal static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
