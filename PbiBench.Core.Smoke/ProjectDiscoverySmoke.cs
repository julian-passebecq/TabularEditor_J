using System;
using System.IO;
using System.Linq;
using System.Threading;
using PbiBench.Core.Project;

internal static class ProjectDiscoverySmoke
{
    internal static void Run(string parent)
    {
        var root = Path.Combine(parent, "Discovery");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "a.pbip"), "{}");
        File.WriteAllText(Path.Combine(root, "b.pbip"), "{}");
        File.WriteAllText(Path.Combine(root, ".git"), "gitdir: ../worktrees/test");
        var context = PbipProjectDiscovery.Discover(root);
        Check(context.PbipFile == null && context.PbipCandidates.Length == 2 && context.Git.Detected, "Ambiguous PBIP/worktree.");
        Check(PbipProjectDiscovery.Discover(Path.Combine(root, "b.pbip")).PbipFile == Path.Combine(root, "b.pbip"), "Explicit PBIP selection.");
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            try { PbipProjectDiscovery.Discover(root, cancellationToken: cts.Token); throw new Exception("Cancellation ignored."); }
            catch (OperationCanceledException) { }
        }
        for (var i = 0; i < PbipProjectDiscovery.MaxEntriesPerDirectory + 5; i++)
            Directory.CreateDirectory(Path.Combine(root, "child" + i));
        Check(PbipProjectDiscovery.Discover(root).DiscoveryIncomplete, "Wide tree limit not disclosed.");
        var deep = Path.Combine(parent, "deep");
        Directory.CreateDirectory(deep);
        File.WriteAllText(Path.Combine(deep, "deep.pbip"), "{}");
        var leaf = deep;
        for (var i = 0; i < 7; i++) { leaf = Path.Combine(leaf, "child"); Directory.CreateDirectory(leaf); }
        Check(PbipProjectDiscovery.Discover(deep).DiscoveryIncomplete, "Depth limit not disclosed.");
        // Drive roots must retain their separator; bounded scan, read-only.
        var driveRoot = Path.GetPathRoot(root)!;
        var drive = PbipProjectDiscovery.Discover(driveRoot);
        Check(Path.IsPathRooted(drive.ProjectRoot) && drive.ProjectRoot == driveRoot, "Filesystem root corrupted.");
        var git = GitWorkingTree.ParsePorcelain("## topic...origin/topic\0 M path with spaces\0R  new\0old\0?? untracked\0");
        Check(git.Entries.Count == 3 && git.Entries[1].Path == "new" && git.Entries[1].OriginalPath == "old" && git.Entries[2].Status == "??", "NUL Git rename/untracked parsing.");
        Console.WriteLine("Project discovery: ambiguity, explicit selection, worktree marker, cancellation, width/depth/root PASS");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
