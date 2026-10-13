using System;
using System.Linq;
using PbiBench.Core.Dax;

internal static class DaxDiagnosticsCorpusSmoke
{
    private static readonly string[] Valid = {
        "SUM(Sales[Amount])",
        "SUM('Sales'[Amount]) + [Total Sales]",
        "CALCULATE([Total Sales], 'O''Brien Accounts'[Account] = \"A\")",
        "'Sales'[Order]]Date]",
        "VAR total = SUM(Sales[Amount]) VAR tax = total * 0.2 RETURN DIVIDE(tax, total, 0)",
        "/* ( outer /* nested } */ ) */ SUMX(Sales, Sales[Quantity] * 2)",
        "EVALUATE ROW(\"v\", 'O''Brien Accounts'[Balance]) -- trailing ( comment",
        "EVALUATE ADDCOLUMNS(VALUES('Date'[Year]), \"Sales Total\", [Total Sales]) ORDER BY [Sales Total] DESC",
        "DEFINE MEASURE Sales[Query Measure] = 1 COLUMN 'Sales'[Query Column] = 2 TABLE Helper = {1} EVALUATE SUMMARIZECOLUMNS('Date'[Year], \"q\", [Query Measure]) EVALUATE Helper",
        "DEFINE FUNCTION Twice = (n : SCALAR VAL) => n * 2 EVALUATE ROW(\"v\", Twice(3), \"w\", Lib.AddOne(1))",
        "(x : SCALAR VAL) => x + Lib.AddOne(x)",
        "RANKX(ALL(Sales), [Total Sales], , DESC, DENSE)",
        "DATEDIFF(MIN('Date'[Date]), MAX('Date'[Date]), DAY) + PERCENTILE.INC(Sales[Amount], 0.5)",
        "SWITCH(TRUE(), [Margin %] > 0.5, \"high\", [Margin %] > 0.2, \"mid\", \"low\")",
        "EVALUATE DATATABLE(\"K\", INTEGER, \"Label\", STRING, {{1, \"a\"}, {2, \"b\"}})",
        "CALCULATE([Total Sales], CROSSFILTER(Sales[Amount], 'O''Brien Accounts'[Amount], BOTH))",
        "EVALUATE SELECTCOLUMNS(Sales, \"Sales[Doubled]\", Sales[Amount] * 2) ORDER BY Sales[Doubled]",
        "IF(ISBLANK([Total Sales]), BLANK(), [Total Sales]); ",
        "\"unterminated? no: \"\" is an escape\"",
        "CALCULATE ( [Total Sales] , KEEPFILTERS ( Sales[Amount] > 0 ) )"
    };

    private static readonly (string text, string code, string reference)[] Invalid = {
        ("SUM(Sales[Amount]", DaxDiagnosticCodes.Delimiter, ""),
        ("{1, 2)", DaxDiagnosticCodes.Delimiter, ""),
        ("SUM(1))", DaxDiagnosticCodes.Delimiter, ""),
        ("\"open string", DaxDiagnosticCodes.Delimiter, ""),
        ("'Open table", DaxDiagnosticCodes.Delimiter, ""),
        ("[Open column", DaxDiagnosticCodes.Delimiter, ""),
        ("/* outer /* nested */ never closed", DaxDiagnosticCodes.Delimiter, ""),
        ("SUM('Missing'[Amount])", DaxDiagnosticCodes.UnknownTable, "Missing"),
        ("SUM(Missing[Amount])", DaxDiagnosticCodes.UnknownTable, "Missing"),
        ("SUM(Sales[Nope])", DaxDiagnosticCodes.UnknownColumnOrMeasure, "Sales[Nope]"),
        ("SUM('O''Brien Accounts'[Quantity])", DaxDiagnosticCodes.UnknownColumnOrMeasure, "O'Brien Accounts[Quantity]"),
        ("[Deleted Measure] + 1", DaxDiagnosticCodes.UnknownColumnOrMeasure, "[Deleted Measure]"),
        ("VAR a = 1 RETURN a + b", DaxDiagnosticCodes.UndeclaredVariable, "b"),
        ("VAR total = 1 RETURN totl", DaxDiagnosticCodes.UndeclaredVariable, "totl"),
        ("DIVIDE(1)", DaxDiagnosticCodes.Arity, "DIVIDE"),
        ("IF(1, 2, 3, 4)", DaxDiagnosticCodes.Arity, "IF"),
        ("BLANK(1)", DaxDiagnosticCodes.Arity, "BLANK"),
        ("Lib.AddOne(1, 2)", DaxDiagnosticCodes.Arity, "Lib.AddOne"),
        ("DEFINE FUNCTION Twice = (n) => n * 2 EVALUATE ROW(\"v\", Twice())", DaxDiagnosticCodes.Arity, "Twice"),
        ("NOTAFUNCTION(1)", DaxDiagnosticCodes.UnknownFunction, "NOTAFUNCTION")
    };

    public static void Run()
    {
        // Vocabulary = catalog names (stand-in for the pinned TE2 vocabulary the host supplies).
        var names = SliceSmokes.SyntheticNames(DaxSignatureCatalog.Default.Entries.Select(e => e.Name).ToArray());
        foreach (var text in Valid)
        {
            var found = DaxDiagnostics.Analyze(text, names);
            SliceSmokes.Check(found.Count == 0, "False positive in valid corpus: " + text + " → " + string.Join(" | ", found));
        }
        foreach (var (text, code, reference) in Invalid)
        {
            var found = DaxDiagnostics.Analyze(text, names);
            SliceSmokes.Check(found.Any(d => d.Code == code && (reference.Length == 0 || d.Reference == reference)),
                "Missing " + code + " " + reference + " for: " + text + " → " + string.Join(" | ", found));
            SliceSmokes.Check(found.All(d => d.Severity == DaxDiagnostic.AdvisoryLabel && d.Message.EndsWith("(advisory)", StringComparison.Ordinal)), "Diagnostics must be labelled advisory.");
        }

        // Without a model only structural checks run (no unknown-object noise for an offline query).
        SliceSmokes.Check(DaxDiagnostics.Analyze("SUM(Anything[Here]) + undeclared", null).Count == 0, "Model checks must not run without a name set.");
        SliceSmokes.Check(DaxDiagnostics.Analyze("DIVIDE(1)", null).Any(d => d.Code == DaxDiagnosticCodes.Arity), "Arity runs offline.");
        // Without a vocabulary the unknown-function hint is suppressed (no claim about newer engine functions).
        SliceSmokes.Check(!DaxDiagnostics.Analyze("NEWERFUNCTION(1)", SliceSmokes.SyntheticNames()).Any(), "Unknown function without vocabulary.");
        // Positions point at the offending token.
        var position = DaxDiagnostics.Analyze("1 + Sales[Nope]", names).Single();
        SliceSmokes.Check(position.Start == 9 && position.Length == 6, "Diagnostic range.");
        // Cross-check with the existing structural helper on the shared scanner tokens.
        foreach (var text in Valid)
            SliceSmokes.Check(DaxAuthoring.CheckStructure(DaxLexicalScanner.ToAuthoringTokens(DaxLexicalScanner.Scan(text))).Count == 0, "Structure helper disagrees: " + text);
        // Bounded output on pathological input.
        SliceSmokes.Check(DaxDiagnostics.Analyze(new string(')', 5000), names).Count == DaxDiagnostics.MaxDiagnostics, "Diagnostics must be capped.");
        Console.WriteLine("DAX diagnostics corpus: " + Valid.Length + " valid / " + Invalid.Length + " invalid, advisory labels, offline mode, caps PASS");
    }
}
