using System;
using System.IO;
using System.Linq;
using PbiBench.Core.Project;

internal static class ProjectIdentitySmoke
{
    internal static void Run(string fixtureRoot)
    {
        var pbip = Path.Combine(fixtureRoot, "Contoso.pbip");
        var a = ProjectIdentity.Create(fixtureRoot, pbip);
        var b = ProjectIdentity.Create(fixtureRoot.ToUpperInvariant() + Path.DirectorySeparatorChar, pbip.ToLowerInvariant());
        if (a.Id != b.Id || a.Id.Length != 16) throw new Exception("Identity is not stable across case/trailing separators.");
        if (a.PbipRelativePath != "Contoso.pbip" || a.PbipContentHash == null || a.DisplayName != "Contoso") throw new Exception("Identity fields wrong.");
        if (ProjectIdentity.Create(fixtureRoot, null).Id == a.Id) throw new Exception("Root-only identity must differ from PBIP identity.");
        ReviewedFilePlanSmoke.Expect(() => ProjectIdentity.Create(fixtureRoot, Path.Combine(Path.GetTempPath(), "Other.pbip")), "PBIP outside root");

        var sandbox = Path.Combine(Path.GetTempPath(), "te2-registry-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var registryPath = Path.Combine(sandbox, "TE2Enhanced", "projects.json");
            var registry = ProjectRegistry.Load(registryPath);
            if (registry.Entries.Count != 0) throw new Exception("Missing registry should load empty.");
            registry.Touch(a, new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc));
            registry.Touch(a, new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Utc));
            registry.Save();
            var reloaded = ProjectRegistry.Load(registryPath);
            if (reloaded.Entries.Count != 1 || reloaded.Find(a.Id)?.LastOpenedUtc.StartsWith("2026-10-13T09:00:00") != true)
                throw new Exception("Registry round-trip/dedupe failed.");
            var json = File.ReadAllText(registryPath);
            if (json.IndexOf("Sales", StringComparison.OrdinalIgnoreCase) >= 0) throw new Exception("Registry must hold paths/hashes only.");
            File.WriteAllText(registryPath, "{ not json");
            if (ProjectRegistry.Load(registryPath).Entries.Count != 0) throw new Exception("Corrupt registry should load empty.");
            var inside = ProjectRegistry.Load(Path.Combine(fixtureRoot, ".te2", "projects.json"));
            ReviewedFilePlanSmoke.Expect(() => inside.Touch(a, DateTime.UtcNow), "registry inside project");
            if (!ProjectRegistry.DefaultPath().EndsWith(Path.Combine("TE2Enhanced", "projects.json"))) throw new Exception("Default registry path wrong.");
        }
        finally { try { Directory.Delete(sandbox, true); } catch (Exception) { } }

        // Distinct state identities: no cross-type equality, only explicit Disk-vs-Baseline comparison.
        var manifest = FileManifest.Capture(fixtureRoot);
        var disk = new DiskStamp(manifest);
        var baseline = new BaselineStamp(manifest, "accepted at open");
        var loaded = new LoadedStamp("Model", "Folder", fixtureRoot, false, DateTime.UtcNow);
        if (disk.Equals(baseline) || ((object)loaded).Equals(disk) || disk.Kind == baseline.Kind) throw new Exception("State identities were equated.");
        if (StateComparison.DiskSinceBaseline(baseline, disk).HasChanges) throw new Exception("Same manifest reported changes.");
        if (!new LiveStamp(false, "", DateTime.UtcNow).Describe().Contains("not connected")) throw new Exception("Live stamp text.");
        Console.WriteLine("ProjectIdentitySmoke: stable id, per-user registry outside repo, distinct stamps PASS");
    }
}

internal static class ExternalChangeSmoke
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "te2-change-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Model.SemanticModel", "definition", "tables"));
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(Path.Combine(root, "obj"));
            Func<string, string> p = rel => Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(p("Model.pbip"), "{}");
            File.WriteAllText(p("Model.SemanticModel/definition/tables/Sales.tmdl"), "table Sales");
            File.WriteAllText(p("Model.SemanticModel/definition/tables/Date.tmdl"), "table Date");
            File.WriteAllText(p("Model.SemanticModel/definition/tables/Dup1.tmdl"), "same");
            File.WriteAllText(p("Model.SemanticModel/definition/tables/Dup2.tmdl"), "same");
            File.WriteAllText(p(".git/index"), "x");
            var before = FileManifest.Capture(root);
            if (before.Entries.Any(e => e.RelativePath.StartsWith(".git")) || before.Entries.Count != 5) throw new Exception("Manifest scope wrong.");

            File.WriteAllText(p("Model.SemanticModel/definition/tables/Sales.tmdl"), "table Sales\n  measure X = 1");
            File.Move(p("Model.SemanticModel/definition/tables/Date.tmdl"), p("Model.SemanticModel/definition/tables/Calendar.tmdl"));
            File.Delete(p("Model.SemanticModel/definition/tables/Dup1.tmdl"));
            File.Delete(p("Model.SemanticModel/definition/tables/Dup2.tmdl"));
            File.WriteAllText(p("Model.SemanticModel/definition/tables/Dup3.tmdl"), "same");
            File.WriteAllText(p("Model.SemanticModel/definition/tables/New.tmdl"), "table New");
            File.WriteAllText(p("obj/noise.txt"), "ignored");
            File.WriteAllText(p(".git/index"), "y");
            var set = ExternalChangeDetector.Diff(before, FileManifest.Capture(root));
            var renamed = set.Changes.Single(c => c.Kind == ExternalChangeKind.Renamed);
            if (!renamed.PreviousPath!.EndsWith("Date.tmdl") || !renamed.Path.EndsWith("Calendar.tmdl")) throw new Exception("Rename heuristic failed.");
            if (set.Count(ExternalChangeKind.Modified) != 1 || set.Count(ExternalChangeKind.Removed) != 2 || set.Count(ExternalChangeKind.Added) != 2)
                throw new Exception("Diff counts wrong (ambiguous duplicate hashes must not pair): " + string.Join("; ", set.Changes));
            if (set.Incomplete) throw new Exception("Small fixture should be complete.");
            if (!FileManifest.IsIgnoredPath("a/.git/x") || !FileManifest.IsIgnoredPath(".x.tmdl.te2tmp-1234") || FileManifest.IsIgnoredPath("a/b.tmdl"))
                throw new Exception("Ignore filter wrong.");
            var same = FileManifest.Capture(root);
            if (ExternalChangeDetector.Diff(same, FileManifest.Capture(root)).HasChanges) throw new Exception("Unchanged folder reported changes.");
            Console.WriteLine("ExternalChangeSmoke: modify/add/remove, unique-hash rename, ignored folders PASS");
        }
        finally { try { Directory.Delete(root, true); } catch (Exception) { } }
    }
}

internal static class VsCodeArgsSmoke
{
    internal static void Run()
    {
        var folder = @"C:\Projects\My Model";
        var args = VsCodeHandoff.BuildArguments(folder, @"C:\Projects\My Model\a.tmdl", 12, 3);
        if (!args.SequenceEqual(new[] { folder, "--goto", @"C:\Projects\My Model\a.tmdl:12:3" })) throw new Exception("Goto args wrong.");
        if (!VsCodeHandoff.BuildArguments(folder, newWindow: true).SequenceEqual(new[] { "--new-window", folder })) throw new Exception("Folder args wrong.");
        ReviewedFilePlanSmoke.Expect(() => VsCodeHandoff.BuildArguments("--disable-extensions"), "option injection");
        ReviewedFilePlanSmoke.Expect(() => VsCodeHandoff.BuildArguments(@"relative\path"), "relative path");
        ReviewedFilePlanSmoke.Expect(() => VsCodeHandoff.BuildArguments(@"\\server\share"), "UNC path");
        ReviewedFilePlanSmoke.Expect(() => VsCodeHandoff.BuildArguments(folder, null, 3), "line without file");
        ReviewedFilePlanSmoke.Expect(() => VsCodeHandoff.BuildArguments(folder, @"C:\a.tmdl", 0), "line zero");
        var line = VsCodeHandoff.ToWindowsCommandLine(new[] { @"C:\My Folder\", "plain", "say \"hi\"", @"C:\x\\" });
        if (line != "\"C:\\My Folder\\\\\" plain \"say \\\"hi\\\"\" C:\\x\\\\") throw new Exception("Windows quoting wrong: " + line);
        ReviewedFilePlanSmoke.Expect(() => VsCodeHandoff.QuoteWindowsArgument("a\nb"), "newline in argument");
        var candidates = VsCodeHandoff.CandidateExecutables(@"C:\Windows;""C:\Program Files\Microsoft VS Code\bin"";relative\bin", @"C:\Users\u\AppData\Local", @"C:\Program Files",
            new[] { @"D:\Tools\VSCode\" });
        if (candidates[0] != @"D:\Tools\VSCode\Code.exe" || candidates[1] != @"C:\Program Files\Microsoft VS Code\Code.exe" ||
            !candidates.Contains(@"C:\Users\u\AppData\Local\Programs\Microsoft VS Code\Code.exe") || candidates.Count != 3 || candidates.Any(c => c.EndsWith(".cmd")))
            throw new Exception("Candidate list wrong: " + string.Join("; ", candidates));
        Console.WriteLine("VsCodeArgsSmoke: argument list, injection refusal, Windows quoting, Code.exe candidates PASS");
    }
}
