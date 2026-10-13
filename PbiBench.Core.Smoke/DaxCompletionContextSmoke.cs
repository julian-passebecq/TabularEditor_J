using System;
using System.Linq;
using PbiBench.Core.Dax;

internal static class DaxCompletionContextSmoke
{
    // '|' marks the caret; it is removed before analysis.
    private static DaxCompletionContext At(string marked)
    {
        var caret = marked.IndexOf('|');
        return DaxCompletionContext.Analyze(marked.Remove(caret, 1), caret);
    }

    private static void Expect(string marked, DaxCaretKind kind, string prefix = "", string table = "")
    {
        var ctx = At(marked);
        SliceSmokes.Check(ctx.Kind == kind, marked + ": expected " + kind + " got " + ctx.Kind);
        SliceSmokes.Check(ctx.Prefix == prefix, marked + ": expected prefix '" + prefix + "' got '" + ctx.Prefix + "'");
        SliceSmokes.Check(ctx.TableName == table, marked + ": expected table '" + table + "' got '" + ctx.TableName + "'");
    }

    public static void Run()
    {
        Expect("EVALUATE |", DaxCaretKind.Code);
        Expect("SU|", DaxCaretKind.Code, "SU");
        Expect("SUMX('Sales'[Am|", DaxCaretKind.InsideTableColumn, "Am", "Sales");
        Expect("SUMX('Sales'[Am|ount], 1)", DaxCaretKind.InsideTableColumn, "Am", "Sales");
        Expect("SUMX(Sales[|", DaxCaretKind.InsideTableColumn, "", "Sales");
        Expect("'O''Brien Accounts'[Bal|", DaxCaretKind.InsideTableColumn, "Bal", "O'Brien Accounts");
        Expect("'Sales'[Order]]D|", DaxCaretKind.InsideTableColumn, "Order]D", "Sales");
        Expect("CALCULATE([Tot|", DaxCaretKind.InsideBracket, "Tot");
        Expect("'O''Br|", DaxCaretKind.InsideQuotedTable, "O'Br");
        Expect("EVALUATE ROW(\"a [b|", DaxCaretKind.InsideString);
        Expect("EVALUATE ROW(\"say \"\"hi|\"\"\", 1)", DaxCaretKind.InsideString);
        Expect("1 -- comment SUM|", DaxCaretKind.InsideComment);
        Expect("/* outer /* nested */ still |comment */ 1", DaxCaretKind.InsideComment);
        Expect("/* outer /* nested */ done */ SU|", DaxCaretKind.Code, "SU");
        Expect("VAR |", DaxCaretKind.VariableDeclaration);
        Expect("VAR to|", DaxCaretKind.VariableDeclaration, "to");
        Expect("VAR x = 1 RETURN |", DaxCaretKind.Code);
        Expect("'Sales'[Amount]|", DaxCaretKind.Code);

        var call = At("SUMX(FILTER(Sales, Sales[Amount] > 0), |");
        SliceSmokes.Check(call.EnclosingFunction == "SUMX" && call.ArgumentIndex == 2 && call.AfterFunctionOpener, "Function opener context.");
        var nested = At("IF(TRUE(), DIVIDE(1, |");
        SliceSmokes.Check(nested.EnclosingFunction == "DIVIDE" && nested.ArgumentIndex == 2, "Nested call context.");
        var table = At("SUMX({1, |");
        SliceSmokes.Check(table.EnclosingFunction == "SUMX" && table.ArgumentIndex == 1, "Table constructor is not a function frame.");
        var commentComma = At("SUMX(T /* , ( */ , |");
        SliceSmokes.Check(commentComma.ArgumentIndex == 2, "Commas/parentheses in comments must not count.");

        var names = SliceSmokes.SyntheticNames("SUM", "SUMX", "PERCENTILE.INC", "NEWFUNCTIONX");
        var vars = At("VAR total = 1 VAR totalTax = 2 RETURN to|");
        var items = vars.Suggest(names, null);
        SliceSmokes.Check(items.Take(2).All(x => x.Kind == "Variable") && items.Any(x => x.InsertText == "totalTax"), "Variables must rank first.");
        SliceSmokes.Check(!At("VAR a = b| VAR later = 1 RETURN a").Variables.Contains("later"), "Later VARs are not in scope.");

        var cols = At("SUMX('O''Brien Accounts'[|").Suggest(names, null);
        SliceSmokes.Check(cols.Select(x => x.InsertText).OrderBy(x => x).SequenceEqual(new[] { "[Account]", "[Amount]", "[Balance]" }), "Table-scoped members only.");
        var escaped = At("'Sales'[Ord|").Suggest(names, null);
        SliceSmokes.Check(escaped.Single().InsertText == "[Order]]Date]", "Bracket escape on insert.");
        var tables = At("EVALUATE 'O|").Suggest(names, null);
        SliceSmokes.Check(tables.Any(x => x.InsertText == "'O''Brien Accounts'"), "Quoted table escape on insert.");
        var bare = At("CALCULATE([|").Suggest(names, null);
        SliceSmokes.Check(bare.Count(x => x.InsertText == "[Amount]") == 1 && bare.First().Kind == "Measure" && bare.Any(x => x.InsertText == "[Margin %]"), "Bare bracket: measures first, columns de-duplicated.");
        var code = At("EVALUATE PERC|").Suggest(names, null);
        SliceSmokes.Check(code.Any(x => x.InsertText == "PERCENTILE.INC(") && !code.Any(x => x.InsertText == "PERCENTILEINC("), "Dotted function insert text.");
        SliceSmokes.Check(At("NEWFUN|").Suggest(names, null).Any(x => x.InsertText == "NEWFUNCTIONX("), "Pinned vocabulary without signature still completes.");
        SliceSmokes.Check(At("Lib.A|").Suggest(names, null).Any(x => x.InsertText == "Lib.AddOne("), "Model UDF completion.");
        SliceSmokes.Check(At("\"|\"").Suggest(names, null).Count == 0 && At("-- |").Suggest(names, null).Count == 0, "No completion in strings/comments.");

        var replace = At("SUMX('Sales'[Am|ount], 1)");
        SliceSmokes.Check(replace.ReplaceStart == 12 && replace.ReplaceLength == 8, "Replace range must cover the whole bracket token.");
        var ident = At("SU|MX(");
        SliceSmokes.Check(ident.ReplaceStart == 0 && ident.ReplaceLength == 4, "Replace range must cover the whole identifier.");

        var udfQuery = "DEFINE FUNCTION Twice = (n : SCALAR VAL) => n * 2 EVALUATE ROW(\"v\", Twice(|";
        var udfCtx = At(udfQuery);
        SliceSmokes.Check(udfCtx.SignatureHelp(names, null).StartsWith("Twice(«n»)", StringComparison.Ordinal), "Query UDF signature help: " + udfCtx.SignatureHelp(names, null));
        SliceSmokes.Check(At("DIVIDE(1, 2, |").SignatureHelp(names, null).StartsWith("DIVIDE(numerator, denominator, «alternateResult?»)", StringComparison.Ordinal), "Catalog signature help.");
        SliceSmokes.Check(At("UNKNOWNFN(1, |").SignatureHelp(names, null).Contains("not a validity check"), "Unknown function help must not imply invalidity.");
        SliceSmokes.Check(At("1 + |").SignatureHelp(names, null) == "No enclosing function call.", "No call context.");
        Console.WriteLine("DAX completion context: caret kinds, quoting, scope, replace ranges, signature help PASS");
    }
}
