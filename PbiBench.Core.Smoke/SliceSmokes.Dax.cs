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
    }

    /// <summary>Synthetic model shared by the S2 Core smokes.</summary>
    internal static DaxModelNameSet SyntheticNames(params string[] vocabulary) => DaxModelNameSet.Create(new[] {
        new DaxTableNames { Name = "Sales", Columns = new[] { "Amount", "Quantity", "Order]Date" }, Measures = new[] { "Total Sales", "Margin %" } },
        new DaxTableNames { Name = "O'Brien Accounts", Columns = new[] { "Account", "Amount" }, Measures = new[] { "Balance" } },
        new DaxTableNames { Name = "Date", Columns = new[] { "Date", "Year" } }
    }, new[] { DaxSignatureCatalog.ParseUserDefined("Lib.AddOne", "(x : SCALAR VAL) => x + 1")! }, vocabulary);

    internal static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
