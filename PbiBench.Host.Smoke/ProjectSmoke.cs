using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using TabularEditor.PbiBench.Project;

internal static class ProjectSmoke
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "te2-git-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            using (var init = Process.Start(new ProcessStartInfo { FileName = "git.exe", WorkingDirectory = root,
                Arguments = "init --quiet", UseShellExecute = false, CreateNoWindow = true }))
            { if (!init.WaitForExit(10000) || init.ExitCode != 0) throw new Exception("Synthetic Git init failed."); }
            File.WriteAllText(Path.Combine(root, "untracked.txt"), "synthetic");
            var snapshot = PbiBenchGitReader.ReadAsync(root, CancellationToken.None).GetAwaiter().GetResult();
            if (snapshot.Entries.Count != 1 || snapshot.Entries[0].Path != "untracked.txt" || snapshot.Entries[0].Status != "??")
                throw new Exception("Actual Git read lost untracked state.");
            Console.WriteLine("Project adapter: real synthetic Git status PASS");
        }
        finally {
            // Only this randomly created fixture; Git can create read-only files on Windows.
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }
}
