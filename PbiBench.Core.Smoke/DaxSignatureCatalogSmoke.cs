using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Dax;

internal static class DaxSignatureCatalogSmoke
{
    public static void Run()
    {
        var catalog = DaxSignatureCatalog.Default;
        SliceSmokes.Check(catalog.Entries.Count >= 180, "Catalog unexpectedly small: " + catalog.Entries.Count);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalog.Entries)
        {
            SliceSmokes.Check(entry.Name.Length > 0 && keys.Add(DaxSignatureCatalog.VocabularyKey(entry.Name)), "Duplicate/empty catalog name " + entry.Name);
            SliceSmokes.Check(entry.Category.Length > 0 && entry.Summary.Length > 0, "Missing category/summary for " + entry.Name);
            SliceSmokes.Check(entry.MinArgs >= 0 && entry.MinArgs <= entry.MaxArgs, "Bad arity range for " + entry.Name);
            var required = entry.Parameters.Count(p => !p.Optional);
            SliceSmokes.Check(entry.MinArgs == required, entry.Name + ": MinArgs " + entry.MinArgs + " != required parameters " + required);
            var repeating = entry.Parameters.Any(p => p.Repeating);
            SliceSmokes.Check(repeating == (entry.MaxArgs == DaxFunctionSignature.Unbounded), entry.Name + ": repeating marker and unbounded arity disagree.");
            if (!repeating) SliceSmokes.Check(entry.MaxArgs == entry.Parameters.Count, entry.Name + ": MaxArgs != parameter count.");
            // A signature exists for every catalog function, and every accepted argument has an active parameter.
            SliceSmokes.Check(entry.Display.StartsWith(entry.Name + "(", StringComparison.Ordinal) && entry.Display.EndsWith(")", StringComparison.Ordinal), "Signature text for " + entry.Name);
            for (var arg = 1; arg <= Math.Min(entry.MaxArgs, entry.Parameters.Count + 3); arg++)
                SliceSmokes.Check(entry.Format(arg).Contains("«") && entry.ActiveParameter(arg) >= 0, entry.Name + ": no active parameter for argument " + arg);
            SliceSmokes.Check(entry.AcceptsArgumentCount(entry.MinArgs) && !entry.AcceptsArgumentCount(entry.MinArgs - 1), entry.Name + " arity acceptance.");
        }
        SliceSmokes.Check(catalog.TryGet("percentile.inc", out var pct) && pct.Name == "PERCENTILE.INC", "Case-insensitive dotted lookup.");
        SliceSmokes.Check(DaxSignatureCatalog.VocabularyKey("Percentile.Inc") == "PERCENTILEINC", "Vocabulary key normalization.");
        catalog.TryGet("CALCULATE", out var calculate);
        SliceSmokes.Check(calculate.Format(5) == "CALCULATE(expression, «filter?…»)", "Repeating parameter must absorb extra arguments: " + calculate.Format(5));
        catalog.TryGet("DIVIDE", out var divide);
        SliceSmokes.Check(divide.Format(4) == "DIVIDE(numerator, denominator, alternateResult?)" && divide.ActiveParameter(4) < 0, "No active parameter past MaxArgs.");

        var udf = DaxSignatureCatalog.ParseUserDefined("Lib.Scale", "( value : SCALAR NUMERIC VAL, factor ) => value * factor");
        SliceSmokes.Check(udf != null && udf.IsUserDefined && udf.MinArgs == 2 && udf.MaxArgs == 2 && udf.Display == "Lib.Scale(value, factor)", "UDF signature parse.");
        SliceSmokes.Check(DaxSignatureCatalog.ParseUserDefined("NotUdf", "SUM(Sales[Amount])") == null, "Non-UDF expression must not parse as UDF.");
        var merged = catalog.With(new[] { udf! });
        SliceSmokes.Check(merged.TryGet("lib.scale", out _) && !catalog.TryGet("Lib.Scale", out _), "UDF merge must not mutate the default catalog.");
        SliceSmokes.Check(DaxAuthoring.ParameterHelp(DaxLexicalScanner.ToAuthoringTokens(DaxLexicalScanner.Scan("SUMX(T, ")), 8).StartsWith("SUMX(table, «expression»)", StringComparison.Ordinal), "Legacy parameter help uses catalog.");
        Console.WriteLine("DAX signature catalog: " + catalog.Entries.Count + " functions, arity invariants, active parameter, UDF parse PASS");
    }
}
