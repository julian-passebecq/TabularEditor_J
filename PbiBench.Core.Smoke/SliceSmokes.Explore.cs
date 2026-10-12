using System;
using System.Linq;
using PbiBench.Core.Dax;
using PbiBench.Core.Explore;
using PbiBench.Core.Optimize;
using PbiBench.Core.Semantic;

// S7: Explore / Optimize (EXP-002 bounded profiling drafts, OPT-001 semantic assertions).
internal static partial class SliceSmokes
{
    static partial void Explore(string fixtureRoot)
    {
        DaxProfileQueryBuilderSmoke();
        SemanticAssertionSmoke();
    }

    private static void ExpectThrows<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception(message);
    }

    private static void DaxProfileQueryBuilderSmoke()
    {
        var names = DaxModelNameSet.Create(new[] {
            new DaxTableNames { Name = "O'Brien Sales", Columns = new[] { "Amount", "Cust]Key" } },
            new DaxTableNames { Name = "Customer", Columns = new[] { "Cust]Key", "Name" } }
        }, null, null);

        var text = DaxProfileQueryBuilder.ColumnProfile("Customer", "Name", ProfileValueKind.Text, 7);
        Check(text.Query.StartsWith(DaxProfileQueryBuilder.DraftNotice, StringComparison.Ordinal), "Profile draft starts with the not-executed notice.");
        Check(text.Query.Contains("TOPN(7, SUMMARIZECOLUMNS('Customer'[Name]") && text.RowLimit == 7, "Top values bounded by TOPN and row limit.");
        Check(!text.Query.Contains("MIN(") && !text.Query.Contains("MAX("), "Text profile has no MIN/MAX.");
        Check(text.Query.Contains("COUNTBLANK('Customer'[Name])") && text.Query.Contains("DISTINCTCOUNT('Customer'[Name])"), "Profile aggregates.");
        Check(text.Query.Split(new[] { "EVALUATE" }, StringSplitOptions.None).Length == 3, "Profile has exactly two EVALUATE results.");

        var num = DaxProfileQueryBuilder.ColumnProfile("O'Brien Sales", "Amount", ProfileValueKind.Numeric);
        Check(num.Query.Contains("MIN('O''Brien Sales'[Amount])") && num.Query.Contains("MAX('O''Brien Sales'[Amount])"), "Numeric MIN/MAX with '' escape.");
        Check(num.RowLimit == DaxProfileQueryBuilder.DefaultTopValues, "Default top values.");
        Check(DaxProfileQueryBuilder.ColumnProfile("Customer", "Name", ProfileValueKind.DateTime).Query.Contains("MIN("), "DateTime MIN/MAX.");

        var cov = DaxProfileQueryBuilder.RelationshipCoverage("O'Brien Sales", "Cust]Key", "Customer", "Cust]Key", 15);
        Check(cov.Query.Contains("EXCEPT(DISTINCT('O''Brien Sales'[Cust]]Key]), DISTINCT('Customer'[Cust]]Key]))"), "Coverage escapes ']' and quotes.");
        Check(cov.Query.Contains("TOPN(15, EXCEPT(") && cov.RowLimit == 15, "Unmatched samples bounded.");

        foreach (var q in new[] { text.Query, num.Query, cov.Query })
        {
            var findings = DaxDiagnostics.Analyze(q, names);
            Check(findings.Count == 0, "Draft must be lexically clean against the model: " + string.Join("; ", findings.Select(f => f.ToString())));
            Check(!q.Contains("\r") && q.Split('\n').Where(l => l.TrimStart().StartsWith("//")).All(l => !l.Contains("'") && !l.Contains("[")),
                "Comments never embed object names.");
        }
        // Invalid input never yields a draft.
        ExpectThrows<ArgumentOutOfRangeException>(() => DaxProfileQueryBuilder.ColumnProfile("T", "C", ProfileValueKind.Text, 0), "Top 0 refused.");
        ExpectThrows<ArgumentOutOfRangeException>(() => DaxProfileQueryBuilder.ColumnProfile("T", "C", ProfileValueKind.Text, 101), "Top 101 refused.");
        ExpectThrows<ArgumentOutOfRangeException>(() => DaxProfileQueryBuilder.RelationshipCoverage("A", "k", "B", "k", 1001), "Sample 1001 refused.");
        ExpectThrows<ArgumentException>(() => DaxProfileQueryBuilder.ColumnProfile("T\nEVALUATE", "C", ProfileValueKind.Text), "Newline in name refused.");
        ExpectThrows<ArgumentException>(() => DaxProfileQueryBuilder.ColumnProfile("T", "", ProfileValueKind.Text), "Empty column refused.");
        ExpectThrows<ArgumentException>(() => DaxProfileQueryBuilder.ColumnProfile("T", "C", (ProfileValueKind)99), "Unknown kind refused.");
        Console.WriteLine("DaxProfileQueryBuilder: bounded drafts, escaping, lexically clean, refusals PASS");
    }

    private static void SemanticAssertionSmoke()
    {
        var input = new AssertionInput {
            Measures = new[] {
                new AssertionMeasure { Id = "m1", Path = "Sales/Total", FormatString = "#,0", Description = "Sum" },
                new AssertionMeasure { Id = "m2", Path = "Sales/No format", Description = "x" },
                new AssertionMeasure { Id = "m3", Path = "Sales/Dynamic", FormatStringExpression = "\"0.0\"", Description = "y" },
                new AssertionMeasure { Id = "m4", Path = "Sales/Hidden", IsHidden = true },
                new AssertionMeasure { Id = "m5", Path = "Sales/No description", FormatString = "0" }
            },
            Columns = new[] {
                new AssertionColumn { Id = "c1", Path = "Sales/CustKey" },
                new AssertionColumn { Id = "c2", Path = "Sales/DateKey", IsHidden = true },
                new AssertionColumn { Id = "c3", Path = "Customer/CustKey" }
            },
            Relationships = new[] {
                new AssertionRelationship { Id = "r1", Name = "Sales_Customer", FromColumnId = "c1", From = "'Sales'[CustKey]", To = "'Customer'[CustKey]", BothDirections = true, Active = true },
                new AssertionRelationship { Id = "r2", Name = "Sales_Date", FromColumnId = "c2", From = "'Sales'[DateKey]", To = "'Date'[Key]", BothDirections = true, Active = true },
                new AssertionRelationship { Id = "r3", Name = "", FromColumnId = "", From = "Unresolved source", To = "'X'[k]" }
            },
            Unresolved = new[] { new SemanticUnresolvedReference { SourceId = "m2", SourcePath = "Sales/No format", Property = "Expression", Code = "DAX002", Message = "Unknown table 'Gone'. (advisory)" } },
            BidirectionalAllowlist = SemanticAssertions.ParseAllowlist(" sales_date \r\n\r\n;  ")
        };
        var report = SemanticAssertions.Evaluate(input);
        AssertionOutcome O(string id) => report.Outcomes.Single(o => o.Definition.Id == id);
        string[] Ids(string id) => report.Findings.Where(f => f.AssertionId == id).Select(f => f.ObjectId).OrderBy(x => x).ToArray();

        Check(report.Outcomes.Count == SemanticAssertions.All.Count && !report.Passed, "Every assertion reported.");
        Check(Ids(SemanticAssertions.FormatString).SequenceEqual(new[] { "m2" }) && O(SemanticAssertions.FormatString).Checked == 4, "Format string: hidden skipped, expression accepted.");
        Check(Ids(SemanticAssertions.UnresolvedReferences).SequenceEqual(new[] { "m2" }), "Unresolved references come from the usage index rows.");
        Check(Ids(SemanticAssertions.Bidirectional).SequenceEqual(new[] { "r1" }), "Allowlist is case-insensitive by name.");
        Check(Ids(SemanticAssertions.HiddenKeys).SequenceEqual(new[] { "c1" }) && O(SemanticAssertions.HiddenKeys).Checked == 2, "Visible many-side key only.");
        Check(Ids(SemanticAssertions.MeasureDescription).SequenceEqual(new[] { "m5" }), "Description on visible measures.");
        Check(report.Summary.StartsWith("0/5", StringComparison.Ordinal), "Summary counts: " + report.Summary);

        input.BidirectionalAllowlist = SemanticAssertions.ParseAllowlist("'Sales'[CustKey] -> 'Customer'[CustKey]\nSales_Date");
        Check(SemanticAssertions.Evaluate(input).Findings.All(f => f.AssertionId != SemanticAssertions.Bidirectional), "Allowlist by From -> To label.");

        var clean = SemanticAssertions.Evaluate(new AssertionInput());
        Check(clean.Passed && clean.Findings.Count == 0 && !clean.Truncated, "Empty model passes.");

        var many = Enumerable.Range(0, SemanticAssertions.MaxFindings + 10)
            .Select(i => new AssertionMeasure { Id = "m" + i, Path = "T/m" + i, FormatString = "0" }).ToArray();
        var capped = SemanticAssertions.Evaluate(new AssertionInput { Measures = many });
        Check(capped.Truncated && capped.Findings.Count == SemanticAssertions.MaxFindings &&
            O2(capped, SemanticAssertions.MeasureDescription).Failed == many.Length, "Findings capped, counts exact.");
        Check(SemanticAssertions.ParseAllowlist(string.Join("\n", Enumerable.Range(0, 400).Select(i => "r" + i))).Count == SemanticAssertions.MaxAllowlistEntries, "Allowlist capped.");
        Console.WriteLine("SemanticAssertions: format/ref/bidi allowlist/hidden keys/description, caps PASS");
    }

    private static AssertionOutcome O2(AssertionReport report, string id) => report.Outcomes.Single(o => o.Definition.Id == id);
}
