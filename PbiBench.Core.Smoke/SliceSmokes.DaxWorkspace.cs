using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PbiBench.Core.Dax;
using PbiBench.Core.Documents;
using PbiBench.Core.Project;

// S3: DAX workspace (DAX-013 multiple documents / external change, DAX-014 DAX Studio handoff).
internal static partial class SliceSmokes
{
    static partial void DaxWorkspace(string fixtureRoot)
    {
        DocumentSetSmoke.Run();
        DaxDocumentIdentitySmoke.Run();
        DaxStudioArgsSmoke.Run();
    }

    internal static void WorkspaceCheck(bool condition, string message) { if (!condition) throw new Exception("S3: " + message); }

    internal static void WorkspaceThrows<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception("S3: expected " + typeof(T).Name + ": " + message);
    }
}

internal static class DocumentSetSmoke
{
    private sealed class Doc : IWorkspaceDocument
    {
        public Doc(string id, string? path = null, bool dirty = false) { Id = id; FilePath = path; IsDirty = dirty; }
        public string Id { get; }
        public string? FilePath { get; }
        public bool IsDirty { get; set; }
    }

    public static void Run()
    {
        var check = (Action<bool, string>)SliceSmokes.WorkspaceCheck;
        var root = Path.Combine(Path.GetTempPath(), "s3-set");
        var set = new DocumentSet<Doc>(4);
        check(set.Active == null && set.Neighbour(1) == null && set.CloseAllOrder().Count == 0, "empty set");
        var a = set.Add(new Doc("a", Path.Combine(root, "a.dax")));
        var b = set.Add(new Doc("b", null, dirty: true));
        var c = set.Add(new Doc("c", Path.Combine(root, "c.dax"), dirty: true), activate: false);
        check(ReferenceEquals(set.Active, b) && set.ActiveIndex == 1, "add activates unless told otherwise");
        check(ReferenceEquals(set.FindByPath(Path.Combine(root, "A.DAX")), a), "path lookup is case-insensitive");
        check(ReferenceEquals(set.FindByPath(Path.Combine(root, "sub", "..", "a.dax")), a), "path lookup normalizes");
        check(set.FindByPath(Path.Combine(root, "a.dax"), except: a) == null, "except excludes the asking document");
        check(set.FindByPath("bad\0path") == null && set.FindByPath(null) == null, "invalid paths match nothing");
        SliceSmokes.WorkspaceThrows<InvalidOperationException>(() => set.Add(new Doc("d", Path.Combine(root, "a.DAX"))), "same file twice");
        SliceSmokes.WorkspaceThrows<InvalidOperationException>(() => set.Add(new Doc("a")), "duplicate id");
        check(set.Dirty.Select(d => d.Id).SequenceEqual(new[] { "b", "c" }), "dirty in tab order");
        check(set.CloseAllOrder().Select(d => d.Id).SequenceEqual(new[] { "b", "a", "c" }), "close-all order: active first then left to right");
        check(set.Neighbour(1)!.Id == "c" && set.Neighbour(2)!.Id == "a" && set.Neighbour(-2)!.Id == "c", "neighbour wraps both ways");
        set.Add(new Doc("d"), activate: false);
        check(!set.CanAdd, "bounded");
        SliceSmokes.WorkspaceThrows<InvalidOperationException>(() => set.Add(new Doc("e")), "full set");
        check(set.Remove("b") && set.Active!.Id == "c", "removing the active document selects the right neighbour");
        check(set.Remove("d") && set.Active!.Id == "c", "removing an inactive document keeps the active one");
        check(set.Remove("c") && set.Active!.Id == "a", "removing the last tab selects the left neighbour");
        check(!set.Remove("missing") && !set.Activate("missing"), "unknown ids are refused");
        check(set.Remove("a") && set.Active == null && set.Count == 0 && set.ActiveIndex == -1, "empty after removing all");
        SliceSmokes.WorkspaceThrows<ArgumentOutOfRangeException>(() => new DocumentSet<Doc>(0), "max must be positive");
        Console.WriteLine("S3 DocumentSet: bounded tabs, unique ids/paths, active neighbour, close-all order PASS");
    }
}

internal static class DaxDocumentIdentitySmoke
{
    public static void Run()
    {
        var check = (Action<bool, string>)SliceSmokes.WorkspaceCheck;
        var folder = Path.Combine(Path.GetTempPath(), "s3-dax-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var first = new DaxDocument();
            var second = new DaxDocument();
            check(first.Id != second.Id && first.Id.Length == 32 && RecoveryStore.IsValidDocumentId(first.Id), "stable unique ids usable as recovery ids");
            check(first.Generation == 0 && first.CheckDisk() == DaxDiskState.NotOnDisk && first.Title == "Untitled.dax", "fresh document");
            var id = first.Id;
            first.New("EVALUATE ROW(\"a\", 1)");
            check(first.Generation == 1 && first.Id == id, "New replaces content, keeps id");
            first.Text += " ";
            var path = Path.Combine(folder, "q.dax");
            first.Save(path);
            check(first.Generation == 1 && first.CheckDisk() == DaxDiskState.Unchanged && first.Title == "q.dax", "save keeps generation");
            first.Open(path);
            check(first.Generation == 2, "Open replaces content");

            File.WriteAllText(path, "// external");
            check(first.CheckDisk() == DaxDiskState.Changed && !first.IsDirty, "external change detected without touching the buffer");
            first.Text = "// mine";
            SliceSmokes.WorkspaceThrows<IOException>(() => first.Save(path), "save over an external change");
            check(first.AcknowledgeExternalChange() == DaxDiskState.Changed && first.IsDirty && first.CheckDisk() == DaxDiskState.Unchanged, "keep mine accepts disk baseline");
            first.Save(path);
            check(File.ReadAllText(path) == "// mine" && !first.IsDirty, "explicit keep allows the overwrite");

            first.Text = "// mine again";
            first.Save(path);
            first.Text = "// unsaved";
            File.Delete(path);
            check(first.CheckDisk() == DaxDiskState.Missing, "deletion detected");
            SliceSmokes.WorkspaceThrows<IOException>(() => first.Save(path), "deleted file is not silently recreated");
            check(first.AcknowledgeExternalChange() == DaxDiskState.Missing && first.IsDirty, "keep after deletion");
            first.Save(path);
            check(File.ReadAllText(path) == "// unsaved", "acknowledged deletion recreates on save");

            first.Text = "// clean";
            first.Save(path);
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                check(first.CheckDisk() == DaxDiskState.Unreadable, "locked file reported as unreadable, not unchanged");
                check(first.AcknowledgeExternalChange() == DaxDiskState.Unreadable && !first.IsDirty, "unreadable acknowledgement changes nothing");
            }
            check(first.CheckDisk() == DaxDiskState.Unchanged, "unlocked file unchanged");

            // Recovery integration: kind "dax" copies keyed by document id, isolated from script copies.
            var store = new RecoveryStore(new RecoveryStoreOptions(Path.Combine(folder, "recovery"), true));
            store.Save(first.Id, "dax", first.Title, "EVALUATE 1", DateTime.UtcNow);
            store.Save("script1", "csharp", "s.csx", "// script", DateTime.UtcNow);
            check(store.List().Count(e => e.Kind == "dax" && e.DocumentId == first.Id && e.IsIntact) == 1 && store.Read(first.Id) == "EVALUATE 1", "dax recovery round trip");
            check(Directory.GetFiles(folder, "*.tmp-*").Length == 0, "no temp files left");
        }
        finally { Directory.Delete(folder, true); }
        Console.WriteLine("S3 DaxDocument: stable id, generation, external change/deletion/lock detection, explicit keep PASS");
    }
}

internal static class DaxStudioArgsSmoke
{
    public static void Run()
    {
        var check = (Action<bool, string>)SliceSmokes.WorkspaceCheck;
        var args = DaxStudioHandoff.BuildArguments(new DaxStudioTarget("localhost:51234", "0f1e2d3c-aaaa-bbbb-cccc-123456789abc"));
        check(args.Count == 2 && args[0] == "/server=localhost:51234" && VsCodeHandoff.ToWindowsCommandLine(args) == "/server=localhost:51234 /database=0f1e2d3c-aaaa-bbbb-cccc-123456789abc", "named /server= /database= arguments (DAX Studio registration shape)");
        args = DaxStudioHandoff.BuildArguments(new DaxStudioTarget(" powerbi://api.powerbi.com/v1.0/myorg/Sales Workspace ", "Sales Model"));
        check(VsCodeHandoff.ToWindowsCommandLine(args) == "\"/server=powerbi://api.powerbi.com/v1.0/myorg/Sales Workspace\" \"/database=Sales Model\"", "XMLA label with spaces is quoted and trimmed");

        var refused = new[]
        {
            new DaxStudioTarget("Data Source=localhost:1;Password=synthetic-secret", "db"),
            new DaxStudioTarget("Provider=MSOLAP", "db"),
            new DaxStudioTarget("localhost", "db;Password=x"),
            new DaxStudioTarget("localhost", "User ID=x"),
            new DaxStudioTarget("", "db"),
            new DaxStudioTarget("localhost", " "),
            new DaxStudioTarget("local\nhost", "db"),
            new DaxStudioTarget("localhost", "a\"b"),
            new DaxStudioTarget(new string('s', DaxStudioHandoff.MaxLabelLength + 1), "db"),
            new DaxStudioTarget("password", "db")
        };
        foreach (var target in refused)
        {
            SliceSmokes.WorkspaceThrows<ArgumentException>(() => DaxStudioHandoff.BuildArguments(target), "refused label " + target.Server.Length);
            var plan = DaxStudioHandoff.Plan(@"C:\Program Files\DAX Studio\DaxStudio.exe", target);
            check(plan.Kind == DaxStudioHandoffKind.SaveAndReport && plan.Arguments.Count == 0 && !plan.Reason.Contains("synthetic-secret"), "refused label never launches or echoes secrets");
        }

        check(DaxStudioHandoff.IsAcceptableExecutable(@"C:\Program Files\DAX Studio\DaxStudio.exe"), "default install accepted");
        check(DaxStudioHandoff.IsAcceptableExecutable(@"d:/tools/daxstudio.exe"), "case-insensitive file name");
        foreach (var bad in new[] { null, "", "DaxStudio.exe", @"\\server\share\DaxStudio.exe", @"C:\Tools\notepad.exe", @"C:\Tools\..\DaxStudio.exe", @"C:\Tools\DaxStudio.exe.cmd" })
            check(!DaxStudioHandoff.IsAcceptableExecutable(bad), "refused executable " + bad);

        var candidates = DaxStudioHandoff.CandidateExecutables(@"E:\Portable\DaxStudio.exe",
            new[] { @"D:\Apps\DAX Studio\", @"D:\Apps\DAX Studio\DaxStudio.exe", "relative", @"\\unc\DAX Studio" },
            @"C:\Users\synthetic\AppData\Local", @"C:\Program Files", @"C:\Program Files (x86)");
        check(candidates.SequenceEqual(new[]
        {
            @"E:\Portable\DaxStudio.exe", @"D:\Apps\DAX Studio\DaxStudio.exe", @"C:\Users\synthetic\AppData\Local\Programs\DAX Studio\DaxStudio.exe",
            @"C:\Program Files\DAX Studio\DaxStudio.exe", @"C:\Program Files (x86)\DAX Studio\DaxStudio.exe"
        }), "candidate order: configured, registry, per-user, machine: " + string.Join(" | ", candidates));

        var notFound = DaxStudioHandoff.Plan(null, new DaxStudioTarget("localhost:1", "db"));
        check(notFound.Kind == DaxStudioHandoffKind.SaveAndReport && notFound.Reason.Contains("not found") && notFound.Executable == null, "missing DAX Studio saves and reports");
        var offline = DaxStudioHandoff.Plan(@"C:\Program Files\DAX Studio\DaxStudio.exe", null);
        check(offline.Kind == DaxStudioHandoffKind.SaveAndReport && offline.Reason.Contains("No live model connection"), "draft mode saves and reports");
        var launch = DaxStudioHandoff.Plan(@"C:\Program Files\DAX Studio\DaxStudio.exe", new DaxStudioTarget("localhost:51234", "Sales"));
        check(launch.Kind == DaxStudioHandoffKind.LaunchConnected && launch.Arguments.SequenceEqual(new[] { "/server=localhost:51234", "/database=Sales" }) &&
            launch.Reason.Contains("no connection string"), "connected launch plan");
        Console.WriteLine("S3 DAX Studio handoff: labels only, connection strings refused, executable rules, fallback plans PASS");
    }
}
