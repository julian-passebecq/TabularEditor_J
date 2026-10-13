using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PbiBench.Core.Dax;
using PbiBench.Core.Project;
using PbiBench.Core.Report;

// S6 Report core smokes (REPORT-001 index + lineage, REPORT-002 reviewed theme apply).
internal static partial class SliceSmokes
{
    static partial void Report(string fixtureRoot)
    {
        var themes = ThemeApplyPlanSmoke.Prepare(); // written early so replaced files are not brand new (OBS-001)
        PbirJsonSmoke.Run();
        PbirIndexSmoke.Run();
        ReportLineageSmoke.Run();
        ThemeApplyPlanSmoke.Run(themes);
    }
}

/// <summary>Synthetic PBIR report written at runtime (no real customer content).</summary>
internal static class PbirFixture
{
    internal const string Prefix = "https://developer.microsoft.com/json-schemas/fabric/item/report/";

    internal static string J(string singleQuoted) => singleQuoted.Replace('\'', '"');

    internal static string Col(string table, string column) => J("{'Column':{'Expression':{'SourceRef':{'Entity':'" + table + "'}},'Property':'" + column + "'}}");
    internal static string Mea(string table, string measure) => J("{'Measure':{'Expression':{'SourceRef':{'Entity':'" + table + "'}},'Property':'" + measure + "'}}");
    internal static string Proj(string field, string queryRef) => J("{'field':") + field + J(",'queryRef':'" + queryRef + "'}");

    internal static string ReportJson(bool withCustomTheme = true, string reportSchema = "3.0.0") => J(
        "{\n  '$schema': '" + Prefix + "definition/report/" + reportSchema + "/schema.json',\n" +
        "  'themeCollection': {\n" +
        "    'baseTheme': {\n      'name': 'CY24SU10',\n      'reportVersionAtImport': {\n        'visual': '1.8.0',\n        'report': '2.0.0',\n        'page': '1.3.0'\n      },\n      'type': 'SharedResources'\n    }" +
        (withCustomTheme ? ",\n    'customTheme': {\n      'name': 'Old_Theme.json',\n      'reportVersionAtImport': {\n        'visual': '1.8.0',\n        'report': '2.0.0',\n        'page': '1.3.0'\n      },\n      'type': 'RegisteredResources'\n    }" : "") +
        "\n  },\n" +
        "  'filterConfig': {\n    'filters': [\n      {\n        'name': 'f-report',\n        'field': ") + Col("Date", "Year") + J(",\n        'type': 'Categorical'\n      }\n    ]\n  },\n" +
        "  'resourcePackages': [\n" +
        "    {\n      'name': 'SharedResources',\n      'type': 'SharedResources',\n      'items': [\n        {\n          'name': 'CY24SU10',\n          'path': 'BaseThemes/CY24SU10.json',\n          'type': 'BaseTheme'\n        }\n      ]\n    },\n" +
        "    {\n      'name': 'RegisteredResources',\n      'type': 'RegisteredResources',\n      'items': [\n" +
        (withCustomTheme ? "        {\n          'name': 'Old_Theme.json',\n          'path': 'Old_Theme.json',\n          'type': 'CustomTheme'\n        },\n" : "") +
        "        {\n          'name': 'logo.png',\n          'path': 'logo.png',\n          'type': 'Image'\n        }\n      ]\n    }\n  ],\n" +
        "  'settings': {\n    'useStylableVisualContainerHeader': true,\n    'exportDataMode': 'AllowSummarized'\n  }\n}\n");

    /// <summary>Writes Sales.Report next to Sales.SemanticModel under root; returns the report folder.</summary>
    internal static string Write(string root, bool unknownVisualSchema = false, bool bomCrLf = false, bool withCustomTheme = true)
    {
        var report = Path.Combine(root, "Sales.Report");
        Directory.CreateDirectory(Path.Combine(root, "Sales.SemanticModel", "definition"));
        void Put(string relative, string text)
        {
            var full = Path.Combine(report, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            if (bomCrLf) text = text.Replace("\n", "\r\n");
            File.WriteAllBytes(full, PbirJson.EncodeUtf8(text, bomCrLf));
        }
        Put("definition.pbir", J("{'$schema':'" + Prefix + "definitionProperties/2.0.0/schema.json','version':'4.0','datasetReference':{'byPath':{'path':'../Sales.SemanticModel'}}}"));
        Put("definition/version.json", J("{'$schema':'" + Prefix + "definition/versionMetadata/1.0.0/schema.json','version':'2.0.0'}"));
        Put("definition/report.json", ReportJson(withCustomTheme));
        Put("StaticResources/RegisteredResources/Old_Theme.json", J("{'name':'Old Theme','dataColors':['#111111']}"));
        Put("StaticResources/RegisteredResources/logo.png", "not really a png");
        Put("definition/pages/pages.json", J("{'$schema':'" + Prefix + "definition/pagesMetadata/1.0.0/schema.json','pageOrder':['p1','p2'],'activePageName':'p1'}"));
        var page = Prefix + "definition/page/2.0.0/schema.json";
        var visual = Prefix + "definition/visualContainer/2.0.0/schema.json";
        Put("definition/pages/p1/page.json", J("{'$schema':'" + page + "','name':'p1','displayName':'Overview','displayOption':'FitToPage','filterConfig':{'filters':[{'name':'f-page','field':") + Col("Product", "Category") + J(",'type':'Categorical'}]}}"));
        Put("definition/pages/p1/visuals/v1/visual.json", J("{'$schema':'" + visual + "','name':'v1','position':{'x':0,'y':0,'z':0,'width':100,'height':100},'visual':{'visualType':'card','query':{'queryState':{'Values':{'projections':[") + Proj(Mea("Sales", "Total Sales"), "Sales.Total Sales") + "]}}}}}");
        Put("definition/pages/p1/visuals/v2/visual.json", J("{'$schema':'" + visual + "','name':'v2','visual':{'visualType':'clusteredBarChart','query':{'queryState':{" +
            "'Category':{'projections':[") + Proj(Col("Product", "Category"), "Product.Category") + J("]}," +
            "'Y':{'projections':[{'field':{'Aggregation':{'Expression':") + Col("Sales", "Amount") + J(",'Function':0}},'queryRef':'Sum(Sales.Amount)'}]}," +
            "'Tooltips':{'projections':[") + Proj(Mea("Sales", "Margin"), "Sales.Margin") + J("]}}," +
            "'sortDefinition':{'sort':[{'field':") + Mea("Sales", "Total Sales") + J(",'direction':'Descending'}]}}}," +
            "'filterConfig':{'filters':[{'name':'f-visual','field':") + Col("Sales", "Amount") + J(",'type':'Advanced'}]}}"));
        Put("definition/pages/p1/visuals/v3/visual.json", J("{'$schema':'" + visual + "','name':'v3','visualGroup':{'displayName':'Group 1','groupMode':'ScaleMode'}}"));
        Put("definition/pages/p2/page.json", J("{'$schema':'" + page + "','name':'p2','displayName':'Details','displayOption':'FitToPage'}"));
        var level = J("{'HierarchyLevel':{'Expression':{'Hierarchy':{'Expression':{'SourceRef':{'Entity':'Date'}},'Hierarchy':'Calendar'}},'Level':'Year'}}");
        var variation = J("{'HierarchyLevel':{'Expression':{'Hierarchy':{'Expression':{'PropertyVariationSource':{'Expression':{'SourceRef':{'Entity':'Sales'}},'Name':'Variation','Property':'OrderDate'}},'Hierarchy':'Date Hierarchy'}},'Level':'Year'}}");
        var alias = J("{'Column':{'Expression':{'SourceRef':{'Source':'s'}},'Property':'X'}}");
        Put("definition/pages/p2/visuals/t1/visual.json", J("{'$schema':'" + visual + "','name':'t1','visual':{'visualType':'tableEx','query':{'queryState':{'Values':{'projections':[") +
            string.Join(",", Proj(Mea("Product", "Total Sales"), "a"), Proj(Mea("Sales", "Amount"), "b"), Proj(Col("Sales", "Profit"), "c"), Proj(level, "d"),
                Proj(variation, "e"), Proj(alias, "f"), Proj(Col("Missing", "X"), "g")) + "]}}}}}");
        Put("definition/pages/p2/visuals/bad/visual.json", "{ \"name\": \"bad\", ");
        if (unknownVisualSchema)
            Put("definition/pages/p2/visuals/unk/visual.json", J("{'$schema':'" + Prefix + "definition/visualContainer/9.9.0/schema.json','name':'unk','visual':{'visualType':'futureVisual'}}"));
        return report;
    }

    internal static DaxModelNameSet Names() => DaxModelNameSet.Create(new[] {
        new DaxTableNames { Name = "Sales", Columns = new[] { "Amount", "OrderDate" }, Measures = new[] { "Total Sales", "Profit" } },
        new DaxTableNames { Name = "Product", Columns = new[] { "Category" } },
        new DaxTableNames { Name = "Date", Columns = new[] { "Year" } } }, null, null);

    internal static Dictionary<string, string[]> Hierarchies() => new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) { ["Date"] = new[] { "Calendar" } };

    internal static string NewRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    internal static void Delete(string root) { try { Directory.Delete(root, true); } catch (Exception) { } }

    internal static void Check(bool condition, string message) { if (!condition) throw new Exception("S6: " + message); }

    internal static void Expect<T>(Action action, string what) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("S6: expected " + typeof(T).Name + " for " + what);
    }
}

internal static class PbirJsonSmoke
{
    internal static void Run()
    {
        var c = (Action<bool, string>)PbirFixture.Check;
        var root = PbirJson.Parse(" {\"a\": \"\\u00e9\\n\\\"x\\\"\", \"b\": [1, -2.5e3, true, false, null], \"c\": {}} ");
        c(root.GetString("a") == "é\n\"x\"", "string escapes");
        var b = root.Get("b")!;
        c(b.Items.Count == 5 && b.Items[1].Value == "-2.5e3" && b.Items[2].Kind == PbirJsonKind.True && b.Items[4].Kind == PbirJsonKind.Null, "array values");
        c(b.Raw(" {\"a\": \"\\u00e9\\n\\\"x\\\"\", \"b\": [1, -2.5e3, true, false, null], \"c\": {}} ") == "[1, -2.5e3, true, false, null]", "raw span");
        foreach (var bad in new[] { "{\"a\":1,}", "[1 2]", "{'a':1}", "\"unterminated", "01", "{\"a\":1} x", "", "[\"\t\"]" })
            PbirFixture.Expect<PbirJsonException>(() => PbirJson.Parse(bad), "malformed " + bad);
        PbirFixture.Expect<PbirJsonException>(() => PbirJson.Parse(new string('[', 200) + new string(']', 200)), "depth limit");
        c(PbirJson.Quote("a\"b\\c\u0001") == "\"a\\\"b\\\\c\\u0001\"", "quote");
        var bytes = PbirJson.EncodeUtf8("{}", true);
        c(bytes.Length == 5 && PbirJson.DecodeUtf8(bytes, out var bom) == "{}" && bom, "BOM round trip");
        Console.WriteLine("PbirJsonSmoke: escapes, spans, malformed/depth refusals, BOM PASS");
    }
}

internal static class PbirIndexSmoke
{
    internal static void Run()
    {
        var root = PbirFixture.NewRoot("te2-pbir-index-");
        try
        {
            var folder = PbirFixture.Write(root);
            var index = PbirReportIndex.Load(folder);
            var c = (Action<bool, string>)PbirFixture.Check;
            c(index.Format == PbirReportFormat.Pbir && index.FormatVersion == "2.0.0", "format/version");
            c(index.SemanticModelPath == "../Sales.SemanticModel", "dataset reference");
            c(index.ReportSchema.Kind == "report" && index.ReportSchema.Version == "3.0.0" && index.ReportSchema.IsWritable, "report schema");
            c(index.Pages.Count == 2 && index.Pages[0].DisplayName == "Overview" && index.Pages[1].DisplayName == "Details", "pages in order");
            c(index.Pages[0].Visuals.Count == 3 && index.Pages[0].Visuals.Single(v => v.Name == "v3").IsGroup, "visuals incl. group");
            c(index.Pages[1].Visuals.Count == 1, "malformed visual skipped");
            c(index.Diagnostics.Any(d => d.Contains("bad/visual.json") && d.Contains("not valid JSON")), "malformed visual diagnostic");
            var v2 = index.Pages[0].Visuals.Single(v => v.Name == "v2");
            c(v2.Fields.Count == 5, "v2 fields: category, aggregation, tooltip, sort, filter = " + v2.Fields.Count);
            c(v2.Fields.Any(f => f.Usage == PbirFieldUsage.Projection && f.Role == "Y" && f.Kind == PbirFieldKind.Column && f.Property == "Amount"), "aggregation reports its column");
            c(v2.Fields.Any(f => f.Usage == PbirFieldUsage.Sort) && v2.Fields.Any(f => f.Usage == PbirFieldUsage.VisualFilter), "sort/visual filter");
            c(index.ReportFilters.Count == 1 && index.Pages[0].Filters.Count == 1, "report/page filters");
            c(index.Theme.BaseThemeName == "CY24SU10" && index.Theme.CustomThemeName == "Old_Theme.json" && index.Theme.CustomThemeFileExists
                && index.Theme.CustomThemePath == "StaticResources/RegisteredResources/Old_Theme.json", "theme info");
            c(index.IsWritable, "known schemas writable: " + index.ReadOnlyReason);
            c(PbirReportIndex.FindReportsFor(Path.Combine(root, "Sales.SemanticModel")).Count == 1, "report found for semantic model");
            c(PbirReportIndex.FindReportsFor(Path.Combine(root, "Other.SemanticModel")).Count == 0, "no report for other model");

            var limited = PbirReportIndex.Load(folder, new PbirIndexLimits { MaxVisualsPerPage = 1 });
            c(limited.Truncated && limited.VisualCount == 1 && limited.Diagnostics.Any(d => d.Contains("only the first 1")), "visual bound");

            var unknownRoot = Path.Combine(root, "u");
            var unknown = PbirReportIndex.Load(PbirFixture.Write(unknownRoot, unknownVisualSchema: true));
            c(!unknown.IsWritable && unknown.ReadOnlyReason!.Contains("unknown") && unknown.Pages[1].Visuals.Any(v => v.VisualType == "futureVisual" && !v.Schema.IsKnown), "unknown schema read-only, still indexed");

            var futureRoot = Path.Combine(root, "f");
            var futureFolder = PbirFixture.Write(futureRoot);
            File.WriteAllText(Path.Combine(futureFolder, "definition", "report.json"), PbirFixture.ReportJson(true, "4.0.0"));
            var future = PbirReportIndex.Load(futureFolder);
            c(!future.IsWritable && !future.ReportSchema.IsKnown && future.Pages.Count == 2, "unknown report schema read-only");

            var legacy = Path.Combine(root, "Legacy.Report");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "report.json"), "{\"sections\":[]}");
            var legacyIndex = PbirReportIndex.Load(legacy);
            c(legacyIndex.Format == PbirReportFormat.PbirLegacy && !legacyIndex.IsWritable && legacyIndex.Pages.Count == 0, "legacy read-only");
            c(PbirSchemaRegistry.Describe("x", "https://example.com/schema.json").Kind == "" && !PbirSchemaRegistry.Describe("x", null).IsKnown, "foreign schema url");
            Console.WriteLine("PbirIndexSmoke: pages/visuals/filters/sort/aggregation, theme, bounds, malformed file, unknown + legacy read-only PASS");
        }
        finally { PbirFixture.Delete(root); }
    }
}

internal static class ReportLineageSmoke
{
    internal static void Run()
    {
        var root = PbirFixture.NewRoot("te2-pbir-lineage-");
        try
        {
            var index = PbirReportIndex.Load(PbirFixture.Write(root));
            var names = PbirFixture.Names();
            var lineage = ReportLineage.Build(index, names, PbirFixture.Hierarchies());
            var c = (Action<bool, string>)PbirFixture.Check;
            ReportLineageEntry E(PbirFieldKind kind, string? table, string name) =>
                lineage.Entries.SingleOrDefault(e => e.Kind == kind && e.Table == table && e.Name == name) ?? throw new Exception("S6: missing lineage entry " + kind + " " + table + " " + name);
            var total = E(PbirFieldKind.Measure, "Sales", "Total Sales");
            c(total.State == ReportLineageState.Resolved && total.Uses == 2 && total.Visuals == 2, "Total Sales resolved, 2 uses in 2 visuals (projection + sort)");
            c(E(PbirFieldKind.Column, "Product", "Category").State == ReportLineageState.Resolved && E(PbirFieldKind.Column, "Product", "Category").Uses == 2, "Category resolved (projection + page filter)");
            c(E(PbirFieldKind.Column, "Sales", "Amount").State == ReportLineageState.Resolved, "aggregated column resolved");
            c(E(PbirFieldKind.Column, "Date", "Year").State == ReportLineageState.Resolved, "report filter resolved");
            c(E(PbirFieldKind.Measure, "Sales", "Margin").State == ReportLineageState.Unresolved, "missing measure unresolved");
            var wrongHome = E(PbirFieldKind.Measure, "Product", "Total Sales");
            c(wrongHome.State == ReportLineageState.Unresolved && wrongHome.Reason.Contains("'Sales'"), "measure on another table: hint");
            c(E(PbirFieldKind.Measure, "Sales", "Amount").State == ReportLineageState.Ambiguous, "measure vs column kind mismatch ambiguous");
            c(E(PbirFieldKind.Column, "Sales", "Profit").State == ReportLineageState.Ambiguous, "column vs measure kind mismatch ambiguous");
            c(E(PbirFieldKind.Hierarchy, "Date", "Calendar").State == ReportLineageState.Resolved, "hierarchy resolved with names");
            c(E(PbirFieldKind.Column, "Sales", "OrderDate").State == ReportLineageState.Resolved, "auto date variation resolves to base column");
            c(E(PbirFieldKind.Column, null, "X").State == ReportLineageState.Ambiguous, "query alias ambiguous");
            c(E(PbirFieldKind.Column, "Missing", "X").State == ReportLineageState.Unresolved, "missing table unresolved");
            c(lineage.Entries.First().State == ReportLineageState.Unresolved && lineage.Entries.Last().State == ReportLineageState.Resolved, "ordering unresolved first");
            c(lineage.ModelRevision == names.Revision && lineage.Count(ReportLineageState.Resolved) == 6 && lineage.Count(ReportLineageState.Unresolved) == 3 && lineage.Count(ReportLineageState.Ambiguous) == 3, "counts r/u/a = "
                + lineage.Count(ReportLineageState.Resolved) + "/" + lineage.Count(ReportLineageState.Unresolved) + "/" + lineage.Count(ReportLineageState.Ambiguous));
            var uses = lineage.MeasureUses();
            c(uses.Count == 1 && uses[ReportLineage.MeasureKey("Sales", "Total Sales")] == 2, "measure uses for Usage tab");
            var noHierarchies = ReportLineage.Build(index, names);
            c(noHierarchies.Entries.Single(e => e.Kind == PbirFieldKind.Hierarchy).State == ReportLineageState.Ambiguous, "hierarchy unchecked without names");
            var renamed = DaxModelNameSet.Create(new[] { new DaxTableNames { Name = "Sales", Measures = new[] { "Revenue" } } }, null, null);
            var after = ReportLineage.Build(index, renamed);
            c(after.ModelRevision != lineage.ModelRevision && after.Entries.Single(e => e.Kind == PbirFieldKind.Measure && e.Table == "Sales" && e.Name == "Total Sales").State == ReportLineageState.Unresolved, "rename makes it unresolved, revision changes");
            Console.WriteLine("ReportLineageSmoke: resolved/unresolved/ambiguous (kind mismatch, alias, other home table, hierarchies, auto date), measure uses, revision PASS");
        }
        finally { PbirFixture.Delete(root); }
    }
}

internal static class ThemeApplyPlanSmoke
{
    private static readonly byte[] Theme = Encoding.UTF8.GetBytes("{\"name\":\"Contoso Blue\",\"dataColors\":[\"#0000FF\",\"#00AAFF\"]}");

    internal sealed class Prepared { internal string Root = "", Main = "", Spare = ""; }

    internal static Prepared Prepare()
    {
        var root = PbirFixture.NewRoot("te2-pbir-theme-");
        return new Prepared { Root = root, Main = PbirFixture.Write(Path.Combine(root, "a"), bomCrLf: true), Spare = PbirFixture.Write(Path.Combine(root, "b"), bomCrLf: true) };
    }

    /// <summary>OBS-001: Windows may refuse File.Replace while another process holds the file without delete sharing;
    /// ReviewedFilePlan rolls back instead of retrying, which is the specified behaviour.</summary>
    internal static bool IsReplaceRefused(Exception ex)
    {
        for (var e = ex.InnerException; e != null; e = e.InnerException)
            if (e is IOException && ((e.HResult & 0xFFFF) == 1175 || e.Message.IndexOf("to be replaced", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
        return false;
    }

    internal static void Run(Prepared prepared)
    {
        var root = prepared.Root;
        var journal = PbirFixture.NewRoot("te2-pbir-journal-");
        var c = (Action<bool, string>)PbirFixture.Check;
        try
        {
            // Apply + exact restore on a BOM/CRLF report.
            // Apply + exact restore on a BOM/CRLF report. Fixtures were written before the other S6 smokes ran (OBS-001).
            if (!ApplyCase(prepared.Main, true))
            {
                Console.WriteLine("ThemeApplyPlanSmoke: OBS-001 observed (replace refused by another process); rolled back byte-exact; repeating once on a separate fixture");
                ApplyCase(prepared.Spare, false);
            }

            bool ApplyCase(string folder, bool tolerate)
            {
                var reportPath = Path.Combine(folder, "definition", "report.json");
                var originalBytes = File.ReadAllBytes(reportPath);
                var index = PbirReportIndex.Load(folder);
                var plan = ThemeApplyPlan.Create(index, Theme);
                c(plan.ThemeFileName == "Contoso_Blue.json" && plan.RemovedThemePath == "StaticResources/RegisteredResources/Old_Theme.json", "theme file names");
                var kinds = plan.Plan.Changes.Select(x => x.Kind + ":" + x.RelativePath).ToArray();
                c(kinds.SequenceEqual(new[] { "Replace:definition/report.json", "Create:StaticResources/RegisteredResources/Contoso_Blue.json", "Delete:StaticResources/RegisteredResources/Old_Theme.json" }), "plan changes " + string.Join(",", kinds));
                c(plan.Plan.Preview().All(p => !p.IsStale), "fresh preview not stale");
                var text = index.ReportJsonText!;
                var collection = index.ReportJsonRoot!.Get("themeCollection")!;
                var packages = index.ReportJsonRoot!.Get("resourcePackages")!;
                var newText = plan.NewReportJson;
                c(newText.StartsWith(text.Substring(0, collection.Start), StringComparison.Ordinal) && newText.EndsWith(text.Substring(packages.End), StringComparison.Ordinal), "bytes outside theme spans unchanged");
                c(!newText.Replace("\r\n", "").Contains("\n"), "CRLF preserved");
                var parsed = PbirJson.Parse(newText);
                c(parsed.Path("themeCollection", "customTheme")!.GetString("name") == "Contoso_Blue.json" && parsed.Path("themeCollection", "baseTheme")!.GetString("name") == "CY24SU10", "themeCollection updated");
                c(parsed.Path("themeCollection", "customTheme", "reportVersionAtImport")!.GetString("report") == "2.0.0", "reportVersionAtImport kept");
                var registered = parsed.Get("resourcePackages")!.Items.Single(p => p.GetString("type") == "RegisteredResources").Get("items")!.Items.Select(i => i.GetString("name")).ToArray();
                c(registered.SequenceEqual(new[] { "logo.png", "Contoso_Blue.json" }), "registered items " + string.Join(",", registered));
                c(plan.BeforeSnippet.Contains("Old_Theme.json") && plan.AfterSnippet.Contains("Contoso_Blue.json"), "snippets");

                ReviewedFilePlanResult result;
                try { result = plan.Plan.Apply(journal); }
                catch (ReviewedFilePlanApplyException ex) when (tolerate && ex.RolledBack && IsReplaceRefused(ex))
                {
                    c(File.ReadAllBytes(reportPath).SequenceEqual(originalBytes) && File.Exists(Path.Combine(folder, "StaticResources", "RegisteredResources", "Old_Theme.json"))
                        && !File.Exists(Path.Combine(folder, "StaticResources", "RegisteredResources", "Contoso_Blue.json")), "OBS-001 rollback byte-exact");
                    return false;
                }
                c(result.FilesWritten == 3 && result.State == ReviewedFileJournalState.Applied, "applied " + result.State);
                var bytesAfter = File.ReadAllBytes(reportPath);
                c(bytesAfter.Length > 3 && bytesAfter[0] == 0xEF, "BOM preserved");
                var reindexed = PbirReportIndex.Load(folder);
                c(reindexed.Theme.CustomThemeName == "Contoso_Blue.json" && reindexed.Theme.CustomThemeFileExists && reindexed.IsWritable, "reindex sees new theme");
                c(!File.Exists(Path.Combine(folder, "StaticResources", "RegisteredResources", "Old_Theme.json")), "old theme removed");
                c(ReviewedFilePlan.Restore(result.JournalPath) == 3, "restore count");
                c(File.ReadAllBytes(reportPath).SequenceEqual(originalBytes), "report.json restored byte-exact");
                c(File.Exists(Path.Combine(folder, "StaticResources", "RegisteredResources", "Old_Theme.json")) && !File.Exists(Path.Combine(folder, "StaticResources", "RegisteredResources", "Contoso_Blue.json")), "theme files restored");
                return true;
            }

            // Stale: report.json edited after review → nothing written.
            var staleFolder = PbirFixture.Write(Path.Combine(root, "s"));
            var stalePlan = ThemeApplyPlan.Create(PbirReportIndex.Load(staleFolder), Theme);
            File.AppendAllText(Path.Combine(staleFolder, "definition", "report.json"), " ");
            PbirFixture.Expect<ReviewedFilePlanStaleException>(() => stalePlan.Plan.Apply(journal), "stale report.json");
            c(!File.Exists(Path.Combine(staleFolder, "StaticResources", "RegisteredResources", "Contoso_Blue.json")) && File.Exists(Path.Combine(staleFolder, "StaticResources", "RegisteredResources", "Old_Theme.json")), "stale apply wrote nothing");

            // Refusals: unknown schema, legacy, invalid theme.
            var unknown = PbirReportIndex.Load(PbirFixture.Write(Path.Combine(root, "u"), unknownVisualSchema: true));
            PbirFixture.Expect<ThemeApplyRefusedException>(() => ThemeApplyPlan.Create(unknown, Theme), "unknown schema");
            var good = PbirReportIndex.Load(PbirFixture.Write(Path.Combine(root, "g")));
            PbirFixture.Expect<ThemeApplyRefusedException>(() => ThemeApplyPlan.Create(good, Encoding.UTF8.GetBytes("{\"dataColors\":[]}")), "theme without name");
            PbirFixture.Expect<ThemeApplyRefusedException>(() => ThemeApplyPlan.Create(good, Encoding.UTF8.GetBytes("{nope")), "invalid theme");
            PbirFixture.Expect<ThemeApplyRefusedException>(() => ThemeApplyPlan.Create(good, new byte[ThemeApplyPlan.MaxThemeBytes + 1]), "oversized theme");

            // Review #5: the derived theme file name collides with a non-theme RegisteredResources item: refused with a clear reason.
            var clashFolder = PbirFixture.Write(Path.Combine(root, "x"));
            var clashReport = Path.Combine(clashFolder, "definition", "report.json");
            File.WriteAllText(clashReport, File.ReadAllText(clashReport).Replace("\"logo.png\"", "\"Brand.json\""), new UTF8Encoding(false));
            var clashIndex = PbirReportIndex.Load(clashFolder);
            try { ThemeApplyPlan.Create(clashIndex, Encoding.UTF8.GetBytes("{\"name\":\"Brand\"}")); throw new Exception("S6: theme file name clash not refused"); }
            catch (ThemeApplyRefusedException ex) { c(ex.Message.Contains("Brand.json") && ex.Message.Contains("Image"), "clash message names file and type: " + ex.Message); }
            c(ThemeApplyPlan.Create(clashIndex, Encoding.UTF8.GetBytes("{\"name\":\"Old Theme\"}")).ThemeFileName == "Old_Theme.json", "reusing the current custom theme name is allowed");

            // No custom theme yet and minified report.json without resourcePackages: customTheme appended, package inserted.
            var minFolder = PbirFixture.Write(Path.Combine(root, "m"), withCustomTheme: false);
            File.WriteAllText(Path.Combine(minFolder, "definition", "report.json"), PbirFixture.J("{'$schema':'" + PbirFixture.Prefix + "definition/report/1.0.0/schema.json','themeCollection':{'baseTheme':{'name':'CY24SU10','type':'SharedResources'}},'settings':{}}"));
            var minPlan = ThemeApplyPlan.Create(PbirReportIndex.Load(minFolder), Theme);
            var min = PbirJson.Parse(minPlan.NewReportJson);
            c(min.Path("themeCollection", "customTheme")!.GetString("type") == "RegisteredResources" && min.Get("resourcePackages")!.Items.Count == 1
                && min.Get("settings") != null && minPlan.RemovedThemePath == null && minPlan.Plan.Changes.Count == 2, "insert path");
            c(ThemeApplyPlan.FileNameFor("  ") == "CustomTheme.json" && ThemeApplyPlan.FileNameFor("a/b\\..c") == "a_b___c.json", "file name sanitizing");
            Console.WriteLine("ThemeApplyPlanSmoke: splice keeps other bytes/BOM/CRLF, apply, byte-exact restore, stale reject, unknown/invalid refusals, insert path PASS");
        }
        finally { PbirFixture.Delete(root); PbirFixture.Delete(journal); }
    }
}
