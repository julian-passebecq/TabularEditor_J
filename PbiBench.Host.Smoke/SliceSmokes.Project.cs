using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PbiBench.Core.Project;
using TabularEditor.PbiBench.Project;
using TabularEditor.TOMWrapper;

// S5 Project host smokes.
internal static partial class SliceSmokes
{
    static partial void EarlyProject(TabularModelHandler handler)
    {
        // The smoke model is unsaved: no disk target, so the check must say so instead of comparing.
        if (PbiBenchSaveTargetTracker.Check(handler).State != SaveTargetCheckState.NotApplicable)
            throw new Exception("Unsaved model must have no save target.");
        SaveConflictSmoke.Run(handler);
    }

    static partial void LateProject()
    {
        ProjectWatcherSmoke.Run();
        VsCodeLauncherSmoke.Run();
        ProjectWindowSmoke.Run();
        QaFixHostSmokes.ProjectLayout();
        QaFixHostSmokes.ProjectDpiScale();
    }
}

internal static class S5Fixture
{
    internal static string Create(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Model.SemanticModel", "definition", "tables"));
        File.WriteAllText(Path.Combine(root, "Model.pbip"), "{}");
        File.WriteAllText(Path.Combine(root, "Model.SemanticModel", "definition", "model.tmdl"), "model Model");
        File.WriteAllText(Path.Combine(root, "Model.SemanticModel", "definition", "tables", "Sales.tmdl"), "table Sales");
        return root;
    }

    internal static void Delete(string root) { try { Directory.Delete(root, true); } catch (Exception) { } }

    /// <summary>Pumps WinForms messages until the task completes (bounded); continuations need the UI thread.</summary>
    internal static void Pump(Task task, int timeoutMs = 20000)
    {
        var clock = Stopwatch.StartNew();
        while (!task.Wait(10))
        {
            Application.DoEvents();
            if (clock.ElapsedMilliseconds > timeoutMs) throw new TimeoutException("Smoke task did not complete.");
        }
        task.GetAwaiter().GetResult();
    }

    internal static void PumpUntil(Func<bool> condition, int timeoutMs)
    {
        var clock = Stopwatch.StartNew();
        using (var tick = new ManualResetEventSlim(false))
            while (!condition())
            {
                Application.DoEvents();
                tick.Wait(10);
                if (clock.ElapsedMilliseconds > timeoutMs) return;
            }
    }
}

internal static class SaveConflictSmoke
{
    internal static void Run(TabularModelHandler handler)
    {
        var root = S5Fixture.Create("te2-save-conflict-");
        try
        {
            var target = Path.Combine(root, "Model.SemanticModel", "definition");
            var none = PbiBenchSaveTargetTracker.Check(handler, ModelSourceType.TMDL, target);
            if (none.State != SaveTargetCheckState.NoBaseline) throw new Exception("Missing baseline must be reported, not assumed clean.");
            if (PbiBenchSaveTargetTracker.Remember(handler, ModelSourceType.TMDL, target, "smoke load") == null) throw new Exception("Baseline not captured.");
            if (PbiBenchSaveTargetTracker.Check(handler, ModelSourceType.TMDL, target).State != SaveTargetCheckState.Clean) throw new Exception("Fresh baseline not clean.");
            File.WriteAllText(Path.Combine(target, "tables", "Sales.tmdl"), "table Sales\n  measure External = 1");
            File.WriteAllText(Path.Combine(target, "tables", "New.tmdl"), "table New");
            var conflict = PbiBenchSaveTargetTracker.Check(handler, ModelSourceType.TMDL, target);
            if (conflict.State != SaveTargetCheckState.ExternalChanges || conflict.Changes.Count(ExternalChangeKind.Modified) != 1 || conflict.Changes.Count(ExternalChangeKind.Added) != 1)
                throw new Exception("External edits under the save target were not reported.");
            using (var review = new PbiBenchSyncReviewForm("Save target review", new[] { "smoke" }, conflict.Changes, conflict.Message, true))
            { var h = review.Handle; if (review.RowCount != 2) throw new Exception("Review rows wrong."); }
            // A different target (Save As) must not reuse the old baseline.
            var bim = Path.Combine(root, "model.bim");
            File.WriteAllText(bim, "{}");
            if (PbiBenchSaveTargetTracker.Check(handler, ModelSourceType.File, bim).State != SaveTargetCheckState.NoBaseline) throw new Exception("Baseline leaked across targets.");
            PbiBenchSaveTargetTracker.Remember(handler, ModelSourceType.File, bim, "smoke");
            File.WriteAllText(Path.Combine(root, "sibling.txt"), "not part of a model.bim target");
            if (PbiBenchSaveTargetTracker.Check(handler, ModelSourceType.File, bim).State != SaveTargetCheckState.Clean) throw new Exception("Single-file target saw siblings.");
            File.WriteAllText(bim, "{ \"changed\": true }");
            if (PbiBenchSaveTargetTracker.Check(handler, ModelSourceType.File, bim).State != SaveTargetCheckState.ExternalChanges) throw new Exception("model.bim edit not reported.");
            if (PbiBenchSaveTargetTracker.Check(handler, ModelSourceType.Database, "server").State != SaveTargetCheckState.NotApplicable) throw new Exception("Database has no disk target.");
            Console.WriteLine("SaveConflictSmoke: load baseline, external TMDL/model.bim edits reported, Save As target isolation PASS (TE2 Save itself not hooked)");
        }
        finally { S5Fixture.Delete(root); }
    }
}

internal static class ProjectWatcherSmoke
{
    internal static void Run()
    {
        var root = S5Fixture.Create("te2-watch-");
        try
        {
            var signals = new System.Collections.Concurrent.ConcurrentQueue<ProjectWatchSignal>();
            using (var arrived = new SemaphoreSlim(0))
            using (var watcher = new PbiBenchProjectWatcher(root, TimeSpan.FromMilliseconds(300)))
            {
                watcher.Settled += s => { signals.Enqueue(s); arrived.Release(); };
                watcher.Start();
                if (watcher.State != ProjectWatcherState.Watching) throw new Exception("Watcher not watching after Start.");
                // Ignored folders only: no signal may name an ignored path. Windows can still deliver a late echo for the
                // fixture's own folders, so the check is on signal content, not on whether a signal arrived (review #6).
                Directory.CreateDirectory(Path.Combine(root, ".git"));
                File.WriteAllText(Path.Combine(root, ".git", "index"), "x");
                File.WriteAllText(Path.Combine(root, ".Sales.tmdl.te2tmp-1234abcd"), "temp");
                arrived.Wait(900);
                var ignored = Drain(signals, arrived).SelectMany(s => s.Paths).Where(p => FileManifest.IsIgnoredPath(p) || p.Contains(".te2tmp-")).ToArray();
                if (ignored.Length > 0) throw new Exception("Ignored paths raised a signal: " + string.Join(",", ignored));
                // A burst of 20 writes collapses into one settled signal that carries every path.
                for (var i = 0; i < 20; i++)
                    File.WriteAllText(Path.Combine(root, "Model.SemanticModel", "definition", "tables", "T" + i + ".tmdl"), "table T" + i);
                // The first signal naming any burst file must name all of them (a stray late echo before it is tolerated).
                var burst = WaitFor(signals, arrived, s => s.Paths.Any(p => p.StartsWith("Model.SemanticModel/definition/tables/T", StringComparison.Ordinal)), 5000);
                if (burst == null) throw new Exception("Watcher did not signal.");
                if (burst.Overflow || burst.EventCount < 20 || Enumerable.Range(0, 20).Any(i => !burst.Paths.Contains("Model.SemanticModel/definition/tables/T" + i + ".tmdl")))
                    throw new Exception("Burst was not debounced into one signal: " + burst.EventCount + "/" + burst.Paths.Count);
                arrived.Wait(900);
                Drain(signals, arrived);
                // Overflow is reported as a rescan signal.
                watcher.InjectOverflow();
                if (WaitFor(signals, arrived, s => s.Overflow, 5000) == null) throw new Exception("Overflow not surfaced.");
                // Review #6: a non-overflow error recreates the FileSystemWatcher (rescan signal) and watching continues.
                watcher.InjectError(new IOException("synthetic watcher error"));
                var errored = WaitFor(signals, arrived, s => s.Overflow, 5000);
                if (errored == null || errored.RootMissing) throw new Exception("Watcher error not surfaced as a rescan.");
                if (watcher.Restarts != 1 || watcher.State != ProjectWatcherState.Watching) throw new Exception("Watcher not recreated after an error: " + watcher.State + "/" + watcher.Restarts);
                File.WriteAllText(Path.Combine(root, "Model.SemanticModel", "definition", "tables", "AfterError.tmdl"), "table AfterError");
                if (WaitFor(signals, arrived, s => s.Paths.Contains("Model.SemanticModel/definition/tables/AfterError.tmdl"), 5000) == null) throw new Exception("Recreated watcher missed a write.");
                Drain(signals, arrived);
                watcher.Dispose();
                if (watcher.State != ProjectWatcherState.Disposed) throw new Exception("Disposed state not reported.");
                File.WriteAllText(Path.Combine(root, "after-dispose.txt"), "x");
                if (arrived.Wait(700)) throw new Exception("Disposed watcher still signalled.");
            }
            // Review #6: the root is deleted or moved: an error then signals RootMissing and the watcher stops with a clear state.
            var gone = S5Fixture.Create("te2-watch-gone-");
            using (var arrived = new SemaphoreSlim(0))
            using (var watcher = new PbiBenchProjectWatcher(gone, TimeSpan.FromMilliseconds(100)))
            {
                ProjectWatchSignal last = null;
                watcher.Settled += s => { last = s; arrived.Release(); };
                S5Fixture.Delete(gone);
                if (Directory.Exists(gone)) throw new Exception("Fixture root could not be removed.");
                watcher.InjectError(new IOException("synthetic: root deleted"));
                if (!arrived.Wait(5000) || last == null || !last.RootMissing) throw new Exception("Missing root not signalled.");
                if (watcher.State != ProjectWatcherState.RootMissing || watcher.Restarts != 0) throw new Exception("Missing root must stop the watcher: " + watcher.State);
            }
            Console.WriteLine("ProjectWatcherSmoke: no ignored path in any signal, burst debounced to one signal, overflow rescan, error recreates watcher, missing root stops, dispose PASS");
        }
        finally { S5Fixture.Delete(root); }
    }

    /// <summary>Waits (bounded) until a signal matching <paramref name="match"/> arrives; earlier non-matching signals are discarded.</summary>
    private static ProjectWatchSignal WaitFor(System.Collections.Concurrent.ConcurrentQueue<ProjectWatchSignal> signals, SemaphoreSlim arrived, Func<ProjectWatchSignal, bool> match, int timeoutMs)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            while (signals.TryDequeue(out var s)) if (match(s)) return s;
            var left = timeoutMs - (int)clock.ElapsedMilliseconds;
            if (left <= 0 || !arrived.Wait(left)) return signals.Where(match).FirstOrDefault();
        }
    }

    private static ProjectWatchSignal[] Drain(System.Collections.Concurrent.ConcurrentQueue<ProjectWatchSignal> signals, SemaphoreSlim arrived)
    {
        var list = new System.Collections.Generic.List<ProjectWatchSignal>();
        while (signals.TryDequeue(out var s)) list.Add(s);
        while (arrived.Wait(0)) { }
        return list.ToArray();
    }
}

internal static class VsCodeLauncherSmoke
{
    internal static void Run()
    {
        var located = PbiBenchVsCodeLauncher.Locate(path => path.EndsWith(@"Programs\Microsoft VS Code\Code.exe", StringComparison.OrdinalIgnoreCase));
        if (located == null || !located.EndsWith("Code.exe")) throw new Exception("Default per-user install not located.");
        if (PbiBenchVsCodeLauncher.Locate(path => false) != null) throw new Exception("Missing VS Code must resolve to null.");
        var info = PbiBenchVsCodeLauncher.BuildStartInfo(@"C:\VS Code\Code.exe", VsCodeHandoff.BuildArguments(@"C:\My Project", @"C:\My Project\a.tmdl", 4));
        if (info.UseShellExecute || info.Arguments != "\"C:\\My Project\" --goto \"C:\\My Project\\a.tmdl:4\"") throw new Exception("Start info wrong: " + info.Arguments);
        foreach (var bad in new[] { @"C:\VS Code\bin\code.cmd", @"C:\Windows\System32\cmd.exe", "Code.exe" })
        {
            try { PbiBenchVsCodeLauncher.BuildStartInfo(bad, new[] { @"C:\x" }); throw new InvalidOperationException(bad); }
            catch (ArgumentException) { }
        }
        if (PbiBenchVsCodeLauncher.Open("relative") == null) throw new Exception("Relative folder must be refused before launch.");
        Console.WriteLine("VsCodeLauncherSmoke: Code.exe resolution, no-shell start info, refusal of cmd/relative PASS (no process started)");
    }
}

internal static class ProjectWindowSmoke
{
    internal static void Run()
    {
        var root = S5Fixture.Create("te2-project-window-");
        var registryDir = Path.Combine(Path.GetTempPath(), "te2-registry-" + Guid.NewGuid().ToString("N"));
        var journalDir = Path.Combine(Path.GetTempPath(), "te2-journal-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var form = new PbiBenchProjectForm("Synthetic", false))
            {
                form.RegistryPath = Path.Combine(registryDir, "projects.json");
                form.WatchDebounce = TimeSpan.FromMilliseconds(200);
                form.LoadedState = () => new LoadedStamp("Synthetic", "UnsavedFile", null, true, DateTime.UtcNow);
                var handle = form.Handle; form.Show(); Application.DoEvents();
                form.SourcePath = Path.Combine(root, "Model.pbip");
                S5Fixture.Pump(form.RefreshProjectAsync());
                if (form.Identity == null || form.Identity.PbipRelativePath != "Model.pbip") throw new Exception("Identity not shown for PBIP: " + form.StatusText);
                if (!form.DetailsText.Contains("Baseline: captured at refresh") || !form.DetailsText.Contains("unsaved TE2 edits") || !form.DetailsText.Contains("never equated"))
                    throw new Exception("Distinct stamps missing from details.");
                if (ProjectRegistry.Load(form.RegistryPath).Find(form.Identity.Id) == null) throw new Exception("Recent registry not updated.");
                if (form.BannerText.Length != 0) throw new Exception("Banner shown without changes.");
                File.WriteAllText(Path.Combine(root, "Model.SemanticModel", "definition", "tables", "Sales.tmdl"), "table Sales // edited in VS Code");
                S5Fixture.PumpUntil(() => form.BannerText.Length > 0, 8000);
                if (form.WatchSignals < 1 || !form.BannerText.Contains("1 modified")) throw new Exception("External change banner missing: '" + form.BannerText + "' signals " + form.WatchSignals);
                form.Close();
            }
            // Journal restore window over a real applied plan.
            var plan = new ReviewedFilePlan(root, "smoke theme");
            plan.AddFromCurrent("Model.pbip", System.Text.Encoding.UTF8.GetBytes("{ \"v\": 2 }"));
            plan.Apply(journalDir);
            using (var restore = new PbiBenchJournalRestoreForm(journalDir, text => DialogResult.OK))
            {
                var handle = restore.Handle;
                if (restore.JournalCount != 1) throw new Exception("Journal not listed.");
                restore.Select(0);
                if (!restore.CanRestore) throw new Exception("Applied journal should be restorable: " + restore.StatusText);
                restore.RestoreSelected();
                if (File.ReadAllText(Path.Combine(root, "Model.pbip")) != "{}" || !restore.StatusText.StartsWith("Restored 1")) throw new Exception("Restore via window failed: " + restore.StatusText);
                restore.Select(0);
                if (restore.CanRestore) throw new Exception("Restored journal must not restore twice.");
            }
            // V1 gap 3: "Delete old journals…" in the restore window: confirmation, completed journals older than 30 days only.
            File.WriteAllText(Path.Combine(root, "interrupted.json"), "before");
            var interrupted = new ReviewedFilePlan(root, "smoke interrupted");
            interrupted.AddFromCurrent("interrupted.json", System.Text.Encoding.UTF8.GetBytes("after"));
            interrupted.Apply(journalDir, new ReviewedFilePlanOptions { BeforeMark = s => throw new IOException("synthetic marker failure") });
            var asked = 0;
            var answer = DialogResult.Cancel;
            using (var restore = new PbiBenchJournalRestoreForm(journalDir, text => DialogResult.OK, text => { asked++; return answer; }))
            {
                var handle = restore.Handle;
                if (restore.JournalCount != 2 || restore.DeleteOldJournals(30) != 0 || asked != 0 || !restore.StatusText.Contains("No completed journals"))
                    throw new Exception("Recent journals must not be offered for deletion: " + restore.StatusText);
                foreach (var dir in Directory.GetDirectories(journalDir)) AgeJournal(dir, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
                if (restore.DeleteOldJournals(30) != 0 || asked != 1 || restore.JournalCount != 2) throw new Exception("Declined confirmation must delete nothing.");
                answer = DialogResult.OK;
                if (restore.DeleteOldJournals(30) != 1 || asked != 2 || restore.JournalCount != 1 || !restore.StatusText.StartsWith("Deleted 1 of 1"))
                    throw new Exception("Old completed journal not deleted (interrupted one must stay): " + restore.StatusText);
            }
            Console.WriteLine("ProjectWindowSmoke: identity, stamps, registry, watcher banner, journal restore window, delete old journals PASS");
        }
        finally { S5Fixture.Delete(root); S5Fixture.Delete(registryDir); S5Fixture.Delete(journalDir); }
    }

    /// <summary>Makes a journal look old: its createdUtc and state marker times move to <paramref name="utc"/>.</summary>
    private static void AgeJournal(string directory, DateTime utc)
    {
        var json = Path.Combine(directory, "journal.json");
        var text = System.Text.RegularExpressions.Regex.Replace(File.ReadAllText(json), "\"createdUtc\":\"[^\"]*\"", "\"createdUtc\":\"" + utc.ToString("o") + "\"");
        File.WriteAllText(json, text);
        foreach (var marker in Directory.GetFiles(directory, "*.marker")) File.SetLastWriteTimeUtc(marker, utc);
    }
}
