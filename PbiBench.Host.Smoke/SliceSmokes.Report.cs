using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PbiBench.Core.Report;
using TabularEditor.PbiBench.Report;
using TabularEditor.PbiBench.Semantic;
using TabularEditor.TOMWrapper;

// S6 Report host smokes (REPORT-001 lineage form + Usage counts, REPORT-002 theme apply form).
internal static partial class SliceSmokes
{
    static partial void EarlyReport(TabularModelHandler handler)
    {
        try
        {
            // Theme fixtures are written first so their files are not brand new when replaced (see OBS-001 note below).
            var themes = ThemeApplySmoke.Prepare();
            ReportFormSmoke.Run(handler);
            ThemeApplySmoke.Run(themes);
        }
        finally { PbiBenchReportUsage.Clear(); }
    }
}

/// <summary>Small synthetic PBIR report against the S6 smoke tables (written at runtime).</summary>
internal static class S6Fixture
{
    private const string Prefix = "https://developer.microsoft.com/json-schemas/fabric/item/report/";
    private static string J(string s) => s.Replace('\'', '"');
    private static string Field(string kind, string table, string name) => J("{'field':{'" + kind + "':{'Expression':{'SourceRef':{'Entity':'" + table + "'}},'Property':'" + name + "'}}}");

    internal static string Write(string root, bool unknownSchema = false)
    {
        var report = Path.Combine(root, "S6.Report");
        void Put(string relative, string text)
        {
            var full = Path.Combine(report, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, text, new UTF8Encoding(false));
        }
        var visual = Prefix + "definition/visualContainer/" + (unknownSchema ? "9.0.0" : "2.0.0") + "/schema.json";
        Put("definition.pbir", J("{'version':'4.0','datasetReference':{'byPath':{'path':'../S6.SemanticModel'}}}"));
        Put("definition/version.json", J("{'$schema':'" + Prefix + "definition/versionMetadata/1.0.0/schema.json','version':'2.0.0'}"));
        Put("definition/report.json", J("{\n  '$schema': '" + Prefix + "definition/report/2.0.0/schema.json',\n  'themeCollection': {\n    'baseTheme': {\n      'name': 'CY24SU10',\n      'type': 'SharedResources'\n    }\n  },\n  'resourcePackages': [\n    {\n      'name': 'SharedResources',\n      'type': 'SharedResources',\n      'items': [\n        {\n          'name': 'CY24SU10',\n          'path': 'BaseThemes/CY24SU10.json',\n          'type': 'BaseTheme'\n        }\n      ]\n    }\n  ]\n}\n"));
        Put("definition/pages/p1/page.json", J("{'$schema':'" + Prefix + "definition/page/2.0.0/schema.json','name':'p1','displayName':'Page 1'}"));
        Put("definition/pages/p1/visuals/v1/visual.json", J("{'$schema':'" + visual + "','name':'v1','visual':{'visualType':'card','query':{'queryState':{'Values':{'projections':[")
            + Field("Measure", "S6 Sales", "S6 Total") + "," + Field("Measure", "S6 Sales", "S6 ReportOnly") + "]}}}}}");
        Put("definition/pages/p1/visuals/v2/visual.json", J("{'$schema':'" + visual + "','name':'v2','visual':{'visualType':'tableEx','query':{'queryState':{'Values':{'projections':[")
            + string.Join(",", Field("Column", "S6 Sales", "Amount"), Field("Measure", "S6 Sales", "S6 Missing"), Field("Column", "S6 Sales", "S6 Total"),
                J("{'field':{'HierarchyLevel':{'Expression':{'Hierarchy':{'Expression':{'SourceRef':{'Entity':'S6 Sales'}},'Hierarchy':'S6 Calendar'}},'Level':'Amount'}}}")) + "]}}}}}");
        return report;
    }

    internal static string NewRoot(string prefix) { var r = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(r); return r; }
    internal static void Delete(string root) { try { Directory.Delete(root, true); } catch (Exception) { } }
    internal static void Check(bool condition, string message) { if (!condition) throw new Exception("S6 host: " + message); }
}

internal static class ReportFormSmoke
{
    internal static void Run(TabularModelHandler handler)
    {
        var model = handler.Model;
        var table = model.AddTable("S6 Sales");
        var root = S6Fixture.NewRoot("te2-s6-report-");
        var journal = S6Fixture.NewRoot("te2-s6-journal-");
        try
        {
            var amount = table.AddDataColumn("Amount", "Amount", null, DataType.Decimal);
            table.AddHierarchy("S6 Calendar", null, amount);
            var total = table.AddMeasure("S6 Total", "SUM('S6 Sales'[Amount])");
            var reportOnly = table.AddMeasure("S6 ReportOnly", "1");
            var unused = table.AddMeasure("S6 Unused", "2");
            S6Fixture.Check(PbiBenchReportBinder.FindReports(handler).Count == 0, "unsaved model has no PBIP report");
            var folder = S6Fixture.Write(root);

            using (var form = new PbiBenchReportForm(() => model, folder, journal))
            {
                var h = form.Handle;
                var lineage = form.Lineage;
                S6Fixture.Check(lineage != null && form.Index.IsWritable && form.CanApplyTheme, "index writable: " + form.SummaryText);
                S6Fixture.Check(lineage.Count(ReportLineageState.Resolved) == 4 && lineage.Count(ReportLineageState.Unresolved) == 1 && lineage.Count(ReportLineageState.Ambiguous) == 1,
                    "lineage counts against the live model: " + form.SummaryText);
                S6Fixture.Check(form.LineageRowCount == 6 && form.VisualRowCount == 2 && form.SummaryText.Contains("4 resolved"), "form rows");
                form.SetFilter(ReportLineageState.Unresolved);
                S6Fixture.Check(form.LineageRowCount == 1, "unresolved filter");
                form.SetFilter(null);
                var hierarchy = lineage.Entries.Single(e => e.Kind == PbirFieldKind.Hierarchy);
                S6Fixture.Check(hierarchy.State == ReportLineageState.Resolved, "hierarchy resolved from TOM hierarchies");
                var row = form.FindLineageRow(e => e.Kind == PbirFieldKind.Measure && e.Name == "S6 Total");
                S6Fixture.Check(form.NavigateLineageRow(row) == total && form.DialogResult == DialogResult.OK, "navigate to measure");
            }

            // Usage tab counts (S2 hook): ReportOnly is kept alive by the report, Unused is not.
            var snapshot = PbiBenchSemanticSnapshot.Build(model);
            snapshot.Index.TryGetId(reportOnly, out var reportOnlyId);
            snapshot.Index.TryGetId(unused, out var unusedId);
            var reportOnlyUsage = snapshot.Usage.Measures.Single(m => m.Measure.Id == reportOnlyId);
            S6Fixture.Check(reportOnlyUsage.ExternalReferences == 1 && reportOnlyUsage.IsUnusedInModel, "report count for model-unused measure");
            S6Fixture.Check(snapshot.Usage.UnusedMeasures.Any(m => m.Measure.Id == unusedId) && !snapshot.Usage.UnusedMeasures.Any(m => m.Measure.Id == reportOnlyId), "unused list honours report usage");
            using (var view = new PbiBenchSemanticViewForm(snapshot))
            {
                var h = view.Handle;
                var unusedRow = snapshot.Usage.UnusedMeasures.ToList().FindIndex(m => m.Measure.Id == unusedId);
                S6Fixture.Check(view.ReportUsageKnown && view.ReportUsesForUnusedRow(unusedRow) == "0" && view.ReportUsageSummary.Contains("S6.Report")
                    && view.UnusedMeasureRowCount == snapshot.Usage.UnusedMeasures.Count(), "Usage tab report column: " + view.ReportUsageSummary);
            }

            // A rename changes the name revision: counts are withheld instead of going stale.
            reportOnly.Name = "S6 ReportOnly Renamed";
            var renamed = PbiBenchSemanticSnapshot.Build(model);
            S6Fixture.Check(renamed.Usage.Measures.All(m => m.ExternalReferences == null), "stale lineage must not feed counts after rename");
            using (var view = new PbiBenchSemanticViewForm(renamed)) { var h = view.Handle; S6Fixture.Check(!view.ReportUsageKnown, "Usage tab unchanged without counts"); }

            // No model: everything unresolved, nothing attached.
            PbiBenchReportUsage.Clear();
            using (var form = new PbiBenchReportForm(() => null, folder, journal))
            {
                var h = form.Handle;
                S6Fixture.Check(form.Lineage.Count(ReportLineageState.Unresolved) == 6 && PbiBenchReportUsage.Label == null, "no model: unresolved, not attached");
            }
            // Unknown schema: read-only, theme apply unavailable.
            using (var form = new PbiBenchReportForm(() => model, S6Fixture.Write(Path.Combine(root, "u"), unknownSchema: true), journal))
            {
                var h = form.Handle;
                S6Fixture.Check(!form.CanApplyTheme && form.SummaryText.Contains("READ-ONLY") && form.LineageRowCount > 0, "unknown schema read-only but indexed");
            }
            Console.WriteLine("ReportFormSmoke: lineage vs live TOM (resolved/unresolved/ambiguous, hierarchy), filter, navigation, Usage tab report counts + rename guard, unknown schema read-only PASS");
        }
        finally
        {
            if (!table.IsRemoved) table.Delete();
            S6Fixture.Delete(root); S6Fixture.Delete(journal);
        }
    }
}

internal static class ThemeApplySmoke
{
    private static readonly byte[] Theme = Encoding.UTF8.GetBytes("{\"name\":\"S6 Synthetic\",\"dataColors\":[\"#123456\"]}");

    internal sealed class Prepared { internal string Root, Journal, Main, Spare, Stale, Unknown; }

    internal static Prepared Prepare()
    {
        var p = new Prepared { Root = S6Fixture.NewRoot("te2-s6-theme-"), Journal = S6Fixture.NewRoot("te2-s6-theme-journal-") };
        p.Main = S6Fixture.Write(Path.Combine(p.Root, "a"));
        p.Spare = S6Fixture.Write(Path.Combine(p.Root, "b"));
        p.Stale = S6Fixture.Write(Path.Combine(p.Root, "s"));
        p.Unknown = S6Fixture.Write(Path.Combine(p.Root, "u"), unknownSchema: true);
        return p;
    }

    /// <summary>OBS-001: Windows may refuse File.Replace on a file another process (scanner/indexer) holds
    /// without delete sharing. ReviewedFilePlan then rolls back instead of retrying; that is correct behaviour.</summary>
    internal static bool IsReplaceRefused(Exception ex)
    {
        for (var e = ex?.InnerException; e != null; e = e.InnerException)
            if (e is IOException && ((e.HResult & 0xFFFF) == 1175 || e.Message.IndexOf("to be replaced", StringComparison.OrdinalIgnoreCase) >= 0)) return true;
        return false;
    }

    internal static void Run(Prepared p)
    {
        try
        {
            if (!ApplyAndRestore(p.Main, p.Journal, true))
            {
                Console.WriteLine("ThemeApplySmoke: OBS-001 observed (replace refused by another process); apply rolled back byte-exact; repeating once on a separate fixture");
                if (!ApplyAndRestore(p.Spare, p.Journal, false)) throw new Exception("S6 host: unreachable");
            }

            using (var form = new PbiBenchThemeApplyForm(PbirReportIndex.Load(p.Stale), p.Journal, text => DialogResult.OK))
            {
                var h = form.Handle;
                form.LoadTheme(Theme, "synthetic");
                File.AppendAllText(Path.Combine(p.Stale, "definition", "report.json"), " ");
                form.Apply();
                S6Fixture.Check(!form.Applied && form.StatusText.StartsWith("Refused, nothing written") && !File.Exists(Path.Combine(p.Stale, "StaticResources", "RegisteredResources", "S6_Synthetic.json")), "stale refused: " + form.StatusText);
            }

            var readOnly = PbirReportIndex.Load(p.Unknown);
            using (var form = new PbiBenchThemeApplyForm(readOnly, p.Journal, text => DialogResult.OK))
            {
                var h = form.Handle;
                form.LoadTheme(Theme, "synthetic");
                S6Fixture.Check(!form.CanApply && form.StatusText.Contains("read-only"), "unknown schema refused: " + form.StatusText);
            }
            Console.WriteLine("ThemeApplySmoke: invalid theme refused, review rows, declined confirm, apply + journal, exact restore, stale refused, unknown schema refused PASS");
        }
        finally { S6Fixture.Delete(p.Root); S6Fixture.Delete(p.Journal); }
    }

    /// <returns>false only when the apply hit OBS-001 and was verified to have rolled back exactly (and tolerate is true).</returns>
    private static bool ApplyAndRestore(string folder, string journal, bool tolerateReplaceRefusal)
    {
        var reportPath = Path.Combine(folder, "definition", "report.json");
        var themePath = Path.Combine(folder, "StaticResources", "RegisteredResources", "S6_Synthetic.json");
        var original = File.ReadAllBytes(reportPath);
        var answer = DialogResult.Cancel;
        using (var form = new PbiBenchThemeApplyForm(PbirReportIndex.Load(folder), journal, text => answer))
        {
            var h = form.Handle;
            form.LoadTheme(Encoding.UTF8.GetBytes("{\"colors\":[]}"), "bad");
            S6Fixture.Check(!form.CanApply && form.StatusText.StartsWith("Refused"), "theme without name refused");
            form.LoadTheme(Theme, "synthetic");
            S6Fixture.Check(form.CanApply && form.ChangeRowCount == 2 && form.AfterText.Contains("S6_Synthetic.json"), "preview rows: " + form.StatusText);
            form.Apply(); // declined confirmation
            S6Fixture.Check(!form.Applied && !File.Exists(themePath) && File.ReadAllBytes(reportPath).SequenceEqual(original), "declined apply wrote nothing");
            answer = DialogResult.OK;
            form.Apply();
            if (!form.Applied && tolerateReplaceRefusal && form.LastApplyFailure != null && form.LastApplyFailure.RolledBack && IsReplaceRefused(form.LastApplyFailure))
            {
                S6Fixture.Check(File.ReadAllBytes(reportPath).SequenceEqual(original) && !File.Exists(themePath), "OBS-001 rollback must be byte-exact");
                return false;
            }
            S6Fixture.Check(form.Applied && File.Exists(themePath) && form.CanRestore && form.JournalPath != null, "apply: " + form.StatusText);
            var after = PbirReportIndex.Load(folder);
            S6Fixture.Check(after.Theme.CustomThemeName == "S6_Synthetic.json" && after.Theme.CustomThemeFileExists, "re-index sees theme");
            form.RestoreLast();
            S6Fixture.Check(File.ReadAllBytes(reportPath).SequenceEqual(original) && !File.Exists(themePath) && form.StatusText.StartsWith("Restored 2"), "exact restore: " + form.StatusText);
        }
        return true;
    }
}
