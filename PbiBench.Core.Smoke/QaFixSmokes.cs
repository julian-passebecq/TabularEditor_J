using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using PbiBench.Core.Dax;
using PbiBench.Core.Navigation;
using PbiBench.Core.Project;
using PbiBench.Core.Report;

/// <summary>Regression checks for the 2026-10-13 Light QA defect packet (D6-D11, D13), synthetic data only.</summary>
internal static class QaFixSmokes
{
    /// <summary>D10 DAX Studio labels, D11 VAR ordering, D13 Quick Open ranking.</summary>
    internal static void Dax()
    {
        foreach (var bad in new[] { "-config", " -x", "/file", "@response.txt", "a\u2028b", "a\u2029b", "srv\u202Ex", "srv\u200Ex", "srv\u200Fx", "srv\u202Ax", "srv\u2066x", "srv\u2069x" })
            Check(DaxStudioHandoff.CheckLabel(bad, "Server") != null, "D10 label refused: " + Escape(bad));
        foreach (var good in new[] { "localhost:51234", "powerbi://api.powerbi.com/v1.0/myorg/WS", "Sales Model", "srv-01" })
            Check(DaxStudioHandoff.CheckLabel(good, "Server") == null, "D10 plain label accepted: " + good);

        var before = DaxDiagnostics.Analyze("EVALUATE VAR a = b VAR b = 1 RETURN ROW(\"x\", a)");
        Check(before.Any(d => d.Code == DaxDiagnosticCodes.UndeclaredVariable && d.Reference == "b"), "D11 VAR used before its declaration is DAX004");
        Check(before.Count(d => d.Code == DaxDiagnosticCodes.UndeclaredVariable) == 1, "D11 only the early use is reported");
        var withModel = DaxDiagnostics.Analyze("VAR a = b VAR b = 1 RETURN a", SliceSmokes.SyntheticNames());
        Check(withModel.Count(d => d.Code == DaxDiagnosticCodes.UndeclaredVariable && d.Reference == "b") == 1, "D11 with a model: reported once");
        Check(!DaxDiagnostics.Analyze("VAR a = 1 VAR b = a + 1 RETURN b").Any(), "D11 ordered VARs are clean");
        Check(!DaxDiagnostics.Analyze("DEFINE VAR q = 1 MEASURE Sales[m] = q + p VAR p = 2 EVALUATE ROW(\"x\", q)").Any(d => d.Reference == "q"), "D11 query-level DEFINE VAR is not ordering-checked");
        Check(!DaxDiagnostics.Analyze("VAR x = SUMX(Sales, 1) VAR y = x RETURN y", SliceSmokes.SyntheticNames()).Any(), "D11 no false positive with a model");

        var objects = new[]
        {
            new SemanticObjectDescriptor { Id = "m:cost", Kind = SemanticObjectKind.Measure, Name = "Total Cost", ParentName = "Sales", DisplayPath = "Sales[Total Cost]" },
            new SemanticObjectDescriptor { Id = "m:sales", Kind = SemanticObjectKind.Measure, Name = "Total Sales", ParentName = "Finance", DisplayPath = "Finance[Total Sales]" },
            new SemanticObjectDescriptor { Id = "m:sales-ly", Kind = SemanticObjectKind.Measure, Name = "Total Sales LY", ParentName = "Sales", DisplayPath = "Sales[Total Sales LY]" },
            new SemanticObjectDescriptor { Id = "t:sales", Kind = SemanticObjectKind.Table, Name = "Sales", DisplayPath = "Sales" }
        };
        var ranked = QuickOpenMatcher.Search(objects, "Total Sales", 10);
        Check(ranked[0].Object.Id == "m:sales" && ranked[0].MatchKind == QuickOpenMatchKind.Exact, "D13 exact name first (got " + ranked[0].Object.Id + ")");
        Check(ranked.Count > 1 && ranked[1].Object.Id == "m:sales-ly", "D13 prefix second");
        Check(QuickOpenMatcher.Search(objects, "total sales", 10)[0].Object.Id == "m:sales", "D13 exact match is case-insensitive");
        Check(QuickOpenMatcher.Search(objects, "Total S", 10)[0].Object.Id != "m:cost", "D13 prefix before token scoring");
        Console.WriteLine("QA fixes D10/D11/D13: PASS");
    }

    /// <summary>D8 reserved device names and wildcards in reviewed file plans.</summary>
    internal static void Project()
    {
        var root = Path.Combine(Path.GetTempPath(), "te2-qa-d8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            foreach (var bad in new[] { "CON", "con.json", "NUL.txt", "aux/x.json", "x/PRN", "COM1.json", "lpt9.tmd", "COM\u00B9.json", "Con .json", "x?.json", "x*.json", "dir/*" })
            {
                Throws<ArgumentException>(() => ReviewedFilePlan.ResolveTarget(root, bad), "D8 ResolveTarget refuses " + bad);
                var plan = new ReviewedFilePlan(root, "qa");
                Throws<ArgumentException>(() => plan.Add(new ReviewedFileChange(bad, ReviewedFileChangeKind.Create, null, new byte[] { 1 })), "D8 plan refuses " + bad);
            }
            foreach (var good in new[] { "CONFIG.json", "console/x.json", "COM10.json", "LPT.json", "nul_safe.txt", "a/b/c.json" })
                ReviewedFilePlan.ResolveTarget(root, good);
        }
        finally { try { Directory.Delete(root, true); } catch (Exception) { } }
        Console.WriteLine("QA fixes D8: PASS");
    }

    /// <summary>D6 invalid UTF-8, D7 reparse points, D9 theme paths.</summary>
    internal static void Report()
    {
        // D6: invalid UTF-8 in any PBIR file is a per-file diagnostic; the other files are still indexed.
        foreach (var file in new[] { "definition/pages/p1/visuals/v1/visual.json", "definition/pages/p2/page.json", "definition/pages/pages.json", "definition/report.json", "definition/version.json", "definition.pbir" })
        {
            var root = PbirFixture.NewRoot("te2-qa-d6-");
            try
            {
                var report = PbirFixture.Write(root);
                File.WriteAllBytes(Path.Combine(report, file.Replace('/', Path.DirectorySeparatorChar)), new byte[] { 0x7B, 0x22, 0xFF, 0xFE, 0x22, 0x7D });
                var index = PbirReportIndex.Load(report);
                Check(index.Diagnostics.Any(d => d.StartsWith(file + ": not valid UTF-8", StringComparison.Ordinal)), "D6 diagnostic for " + file);
                Check(index.Pages.Count >= 1, "D6 other pages still indexed for " + file);
                if (file != "definition/pages/p1/visuals/v1/visual.json") Check(index.Pages.Any(p => p.Visuals.Any(v => v.Name == "v1")), "D6 v1 still indexed for " + file);
            }
            finally { PbirFixture.Delete(root); }
        }

        // D7: a junction under pages is skipped with a diagnostic; outside content is never read.
        {
            var root = PbirFixture.NewRoot("te2-qa-d7-");
            try
            {
                var report = PbirFixture.Write(root);
                var outside = Path.Combine(root, "outside");
                Directory.CreateDirectory(outside);
                File.WriteAllText(Path.Combine(outside, "page.json"), PbirFixture.J("{'$schema':'" + PbirFixture.Prefix + "definition/page/2.0.0/schema.json','name':'jp','displayName':'Outside'}"));
                var junction = Path.Combine(report, "definition", "pages", "jp");
                if (MakeJunction(junction, outside))
                {
                    var index = PbirReportIndex.Load(report);
                    Check(index.Pages.All(p => p.DisplayName != "Outside"), "D7 junction content not indexed");
                    Check(index.Diagnostics.Any(d => d.Contains("definition/pages/jp") && d.Contains("reparse point")), "D7 reparse diagnostic");
                    Check(index.Pages.Any(p => p.Name == "p1"), "D7 real pages still indexed");
                    Directory.Delete(junction); // removes the link only
                }
                else Console.WriteLine("QA fixes D7: junction could not be created here; reparse check NOT RUN");
            }
            finally { PbirFixture.Delete(root); }
        }

        // D9: escaping, rooted, stream-like or device theme paths are refused with ThemeApplyRefusedException.
        foreach (var badPath in new[] { "../../definition/report.json", "C:\\\\Windows\\\\win.ini", "x:y", "NUL", "sub/../../x.json", "x?.json" })
        {
            var root = PbirFixture.NewRoot("te2-qa-d9-");
            try
            {
                var report = PbirFixture.Write(root);
                var reportJson = Path.Combine(report, "definition", "report.json");
                File.WriteAllText(reportJson, File.ReadAllText(reportJson).Replace("\"path\": \"Old_Theme.json\"", "\"path\": \"" + badPath + "\""));
                var before = File.ReadAllBytes(reportJson);
                var index = PbirReportIndex.Load(report);
                var theme = Encoding.UTF8.GetBytes(PbirFixture.J("{'name':'QA Theme','dataColors':['#123456']}"));
                Throws<ThemeApplyRefusedException>(() => ThemeApplyPlan.Create(index, theme), "D9 theme path refused: " + badPath);
                Check(File.ReadAllBytes(reportJson).SequenceEqual(before), "D9 nothing written: " + badPath);
            }
            finally { PbirFixture.Delete(root); }
        }
        {
            var root = PbirFixture.NewRoot("te2-qa-d9-");
            try
            {
                var index = PbirReportIndex.Load(PbirFixture.Write(root));
                Throws<ThemeApplyRefusedException>(() => ThemeApplyPlan.Create(index, Encoding.UTF8.GetBytes("{\"name\":\"CON\"}")), "D9 theme named CON refused");
            }
            finally { PbirFixture.Delete(root); }
        }
        Console.WriteLine("QA fixes D6/D7/D9: PASS");
    }

    private static bool MakeJunction(string link, string target)
    {
        try
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(target);
            using var process = Process.Start(start);
            if (process == null) return false;
            process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd();
            process.WaitForExit(10000);
            return process.ExitCode == 0 && (File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception) { return false; }
    }

    private static string Escape(string s) => string.Concat(s.Select(c => c < 128 && !char.IsControl(c) ? c.ToString() : "\\u" + ((int)c).ToString("X4")));

    private static void Throws<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        catch (Exception e) { throw new Exception(message + ": expected " + typeof(T).Name + ", got " + e.GetType().Name + ": " + e.Message); }
        throw new Exception(message + ": no exception");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception("QA fix: " + message); }
}
