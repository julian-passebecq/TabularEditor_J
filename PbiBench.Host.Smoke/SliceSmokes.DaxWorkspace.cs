using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using PbiBench.Core.Documents;
using TabularEditor.Dax;
using TabularEditor.PbiBench.Dax;

// S3: DAX workspace (DAX-013 multiple documents, recovery, external change; DAX-014 DAX Studio handoff).
internal static partial class SliceSmokes
{
    static partial void LateDaxWorkspace()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pbibench-s3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var prior = DaxFormatterProxy.Instance.Transport;
        var consent = RemoteFormatterConsent.Enabled;
        try
        {
            MultiDocumentSmoke.Run(folder);
            DaxRecoverySmoke.Run(folder);
            DaxExternalChangeSmoke.Run(folder);
            DaxStudioLauncherSmoke.Run(folder);
        }
        finally
        {
            DaxFormatterProxy.Instance.Transport = prior;
            RemoteFormatterConsent.SetEnabled(consent);
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}

/// <summary>Shared fixtures for the S3 host smokes.</summary>
internal static class S3
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    internal static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    internal static void Handles(Control control) { var handle = control.Handle; foreach (Control child in control.Controls) Handles(child); }
    internal static void Check(bool condition, string message) { if (!condition) throw new Exception("S3 host: " + message); }
    internal static void Wait(Func<bool> done)
    {
        var timer = Stopwatch.StartNew();
        while (!done())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(10)) throw new TimeoutException("S3 fixture did not complete.");
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }

    internal static PbiBenchDaxWorkbenchForm NewForm(DocumentDialogs dialogs, WorkspaceDialogs workspace, string recoveryRoot)
    {
        var form = new PbiBenchDaxWorkbenchForm(null, "", "offline", dialogs);
        Handles(form);
        form.WorkspaceDialogs = workspace;
        form.RecoveryRoot = recoveryRoot;
        form.DaxStudioSettingsFolder = Path.Combine(recoveryRoot + "-settings");
        return form;
    }

    internal sealed class DocumentDialogs : IDaxDocumentDialogs
    {
        internal string Open, Save;
        internal readonly Queue<DialogResult> Answers = new Queue<DialogResult>();
        internal DialogResult DefaultAnswer = DialogResult.No;
        internal readonly List<string> Prompted = new List<string>();
        internal int Errors;
        public string OpenPath(IWin32Window owner) => Open;
        public string SavePath(IWin32Window owner, string currentPath) => Save;
        public DialogResult SaveChanges(IWin32Window owner, string name) { Prompted.Add(name); return Answers.Count > 0 ? Answers.Dequeue() : DefaultAnswer; }
        public void Error(IWin32Window owner, string message) { Errors++; }
    }

    internal sealed class WorkspaceDialogs : IDaxWorkspaceDialogs
    {
        internal bool OptIn, Restore, Reload;
        internal string Disclosure, ReportText, Executable;
        internal int Offers, Reports;
        public bool ConfirmRecoveryOptIn(IWin32Window owner, string disclosure) { Disclosure = disclosure; return OptIn; }
        public bool OfferRestore(IWin32Window owner, int count, string folder) { Offers++; return Restore; }
        public bool ConfirmReloadDiscardingEdits(IWin32Window owner, string name) => Reload;
        public string ChooseDaxStudioExecutable(IWin32Window owner) => Executable;
        public void Report(IWin32Window owner, string title, string message) { Reports++; ReportText = message; }
    }

    internal sealed class HeldTransport : IDaxFormatterTransport, IDisposable
    {
        internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
        internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
        internal string Response = "{\"formatted\":\"FORMATTED\",\"errors\":[]}";
        public string Prime(string uri, int timeout) => uri;
        public string Post(string uri, string body, int timeout) { Entered.Set(); if (!Release.Wait(10000)) throw new TimeoutException("Fixture watchdog"); return Response; }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }

    /// <summary>Starts a held remote format on the active tab and returns the transport to release.</summary>
    internal static HeldTransport BeginFormat(PbiBenchDaxWorkbenchForm form)
    {
        var transport = new HeldTransport();
        DaxFormatterProxy.Instance.Transport = transport;
        RemoteFormatterConsent.SetEnabled(true);
        Invoke(form, "FormatDraft", false);
        Wait(() => transport.Entered.IsSet);
        return transport;
    }

    internal static void EndFormat(PbiBenchDaxWorkbenchForm form, HeldTransport transport)
    {
        transport.Release.Set();
        Wait(() => !Field<bool>(form, "_formatting"));
        transport.Dispose();
    }
}

internal static class MultiDocumentSmoke
{
    internal static void Run(string folder)
    {
        var a = Path.Combine(folder, "a.dax"); var b = Path.Combine(folder, "b.dax");
        File.WriteAllText(a, "EVALUATE ROW(\"a\", 1)"); File.WriteAllText(b, "EVALUATE ROW(\"b\", 2)");
        var dialogs = new S3.DocumentDialogs();
        var workspace = new S3.WorkspaceDialogs();
        using (var form = S3.NewForm(dialogs, workspace, Path.Combine(folder, "recovery-multi")))
        {
            var sample = form.ActiveTab;
            S3.Check(form.Tabs.Count == 1 && !sample.Document.IsDirty && sample.Editor.Text.StartsWith("EVALUATE"), "one pristine sample tab at start");
            var tabA = form.OpenInNewTab(a);
            S3.Check(form.Tabs.Count == 2 && ReferenceEquals(form.ActiveTab, tabA), "open adds and activates a tab");
            S3.Check(ReferenceEquals(S3.Field<RichTextBox>(form, "_queryEditor"), tabA.Editor) && ReferenceEquals(S3.Field<DaxDocument>(form, "_document"), tabA.Document),
                "active editor/document follow the tab");
            S3.Check(form.Text.StartsWith("a.dax") && form.Text.Contains("(2/2)"), "title shows active document and position: " + form.Text);
            form.ActivateTab(sample);
            S3.Check(ReferenceEquals(form.OpenInNewTab(a.ToUpperInvariant()), tabA) && form.Tabs.Count == 2, "an open file is activated, never opened twice");
            dialogs.Open = a;
            form.ActivateTab(sample);
            S3.Invoke(form, "OpenDocument");
            S3.Check(ReferenceEquals(form.ActiveTab, tabA) && sample.Document.FilePath == null, "open-in-current of a file open elsewhere switches tabs instead");
            var tabB = form.OpenInNewTab(b);
            var empty = form.NewTab();
            S3.Check(form.Tabs.Count == 4 && empty.Document.FilePath == null && empty.Editor.Text == "", "new empty tab");
            var reused = form.OpenInNewTab(Path.Combine(folder, "missing.dax"));
            S3.Check(reused == null && dialogs.Errors == 1 && form.Tabs.Count == 4, "failed open adds nothing and reports once");
            File.WriteAllText(Path.Combine(folder, "c.dax"), "EVALUATE ROW(\"c\", 3)");
            var c = form.OpenInNewTab(Path.Combine(folder, "c.dax"));
            S3.Check(ReferenceEquals(c, empty) && form.Tabs.Count == 4 && empty.Document.FilePath != null, "pristine empty tab is reused for open");
            var stamps = form.Tabs.Select(t => t.Generation).ToArray();
            S3.Check(stamps.Distinct().Count() == stamps.Length, "tab generation stamps are unique");

            // Per-tab editors: edits and undo stay in their own tab.
            form.ActivateTab(tabB);
            tabB.Editor.Text = "EVALUATE ROW(\"b\", 22)";
            S3.Check(tabB.Document.IsDirty && !tabA.Document.IsDirty && tabA.Editor.Text == "EVALUATE ROW(\"a\", 1)", "edits stay in their tab");
            S3.Check(tabB.Page.Text.EndsWith(" *") && !tabA.Page.Text.EndsWith(" *"), "dirty marker per tab");

            // S001 guard per tab: response keyed by document id lands in the tab that asked, even after switching.
            form.ActivateTab(tabA);
            var held = S3.BeginFormat(form);
            form.ActivateTab(tabB);
            tabB.Editor.Text += " ";
            S3.EndFormat(form, held);
            S3.Check(tabA.Editor.Text == "FORMATTED" && tabA.Editor.CanUndo && tabB.Editor.Text == "EVALUATE ROW(\"b\", 22) ", "response applied to its own tab only");
            S3.Check(ReferenceEquals(form.ActiveTab, tabB), "a background response does not steal focus");
            tabA.Editor.Undo();
            S3.Check(tabA.Editor.Text == "EVALUATE ROW(\"a\", 1)", "one undo restores the background-formatted tab");

            // Edit in the target tab after the request: discarded.
            form.ActivateTab(tabA);
            held = S3.BeginFormat(form);
            tabA.Editor.Text = "edited";
            S3.EndFormat(form, held);
            S3.Check(tabA.Editor.Text == "edited", "stale response discarded after an edit in its tab");

            // Replacement in the target tab (other tab switched in between): discarded.
            held = S3.BeginFormat(form);
            form.ActivateTab(c);
            form.ActivateTab(tabA);
            dialogs.DefaultAnswer = DialogResult.No;
            S3.Invoke(form, "NewDocument");
            S3.EndFormat(form, held);
            S3.Check(tabA.Editor.Text == "" && !tabA.Document.IsDirty, "stale response discarded after a replacement");

            // Closing the requesting tab: discarded (id no longer open), nothing thrown.
            form.ActivateTab(tabB);
            held = S3.BeginFormat(form);
            dialogs.Answers.Enqueue(DialogResult.No);
            S3.Check(form.CloseTab(tabB), "close dirty tab after Don't save");
            S3.EndFormat(form, held);
            S3.Check(form.FindTab(tabB.Document.Id) == null && form.StatusText.Contains("discarded") && form.Tabs.All(t => t.Editor.Text != "FORMATTED"),
                "response for a closed tab discarded: " + form.StatusText);

            // Completion captured in one tab is never applied in another.
            form.ActivateTab(c);
            c.Editor.Select(c.Editor.TextLength, 0);
            var session = form.BeginWorkbenchCompletion();
            form.ActivateTab(sample);
            S3.Check(!form.TryApplyWorkbenchCompletion(session, new DaxAuthoringItem { Name = "x", InsertText = "x" }, out var reason) && sample.Editor.Text.StartsWith("EVALUATE"),
                "cross-tab completion rejected: " + reason);
            form.ActivateTab(c);
            S3.Check(form.TryApplyWorkbenchCompletion(form.BeginWorkbenchCompletion(), new DaxAuthoringItem { Name = "x", InsertText = " " }, out reason), "same-tab completion still applies: " + reason);

            // Results stay with their tab.
            var result = new DaxQueryResult { State = DaxQueryExecutionState.Completed, Columns = new[] { "v" }, Rows = new[] { new object[] { 1 } } };
            S3.Invoke(form, "BindExecutionResult", result);
            form.ActivateTab(sample);
            S3.Check(S3.Field<DataGridView>(form, "_results").RowCount == 0 && !S3.Field<Button>(form, "_exportButton").Enabled, "other tab shows its own (empty) results");
            form.ActivateTab(c);
            S3.Check(S3.Field<DataGridView>(form, "_results").RowCount == 1 && S3.Field<Button>(form, "_exportButton").Enabled, "results restored on return");

            // Tab cycling.
            var before = form.ActiveTab;
            S3.Invoke(form, "SelectNeighbourTab", 1);
            S3.Invoke(form, "SelectNeighbourTab", -1);
            S3.Check(ReferenceEquals(form.ActiveTab, before), "Ctrl+Tab / Ctrl+Shift+Tab cycle");

            // Close all: the active dirty tab is asked first; Cancel closes nothing.
            sample.Editor.Text += " ";
            tabA.Editor.Text = "dirty a";
            form.ActivateTab(tabA);
            dialogs.Prompted.Clear();
            dialogs.Answers.Enqueue(DialogResult.No); dialogs.Answers.Enqueue(DialogResult.Cancel);
            var count = form.Tabs.Count;
            S3.Check(!form.CloseAllTabs() && form.Tabs.Count == count && tabA.Document.IsDirty && sample.Document.IsDirty, "cancel in close-all closes nothing");
            S3.Check(dialogs.Prompted.Count == 2 && dialogs.Prompted[0] == "Untitled.dax" && ReferenceEquals(form.ActiveTab, sample), "prompts follow close-all order with the prompted tab visible");

            // Form close with several dirty tabs: Cancel keeps all; Don't save closes.
            dialogs.Answers.Clear();
            dialogs.Answers.Enqueue(DialogResult.Cancel);
            var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
            S3.Invoke(form, "OnWorkbenchClosing", form, closing);
            S3.Check(closing.Cancel && !S3.Field<bool>(form, "_closing") && form.Tabs.Count == count, "window close cancelled keeps every tab");
            dialogs.DefaultAnswer = DialogResult.No;
            S3.Check(form.CloseAllTabs() && form.Tabs.Count == 1 && form.ActiveTab.Editor.Text == "" && !form.ActiveTab.Document.IsDirty, "close all leaves one empty draft");
            S3.Check(form.CloseTab(form.ActiveTab) && form.Tabs.Count == 1, "closing the last tab leaves an empty draft");

            // Bound: 32 tabs.
            while (form.Tabs.Count < DocumentSet<DaxDocument>.DefaultMaxDocuments) S3.Check(form.NewTab() != null, "tab under the bound");
            S3.Check(form.NewTab() == null && form.StatusText.Contains("At most 32"), "33rd tab refused with a clear status");
            S3.Check(form.SaveAll() == 0 && form.StatusText == "Nothing to save.", "save all with nothing dirty");
        }
        Console.WriteLine("S3 multi-document: tabs, single open per file, per-tab editors/results, id-keyed formatter guard, cross-tab completion, close-all/cancel PASS");
    }
}

internal static class DaxRecoverySmoke
{
    internal static void Run(string folder)
    {
        var root = Path.Combine(folder, "recovery");
        var marker = root + ".dax.optin";
        var dialogs = new S3.DocumentDialogs();
        var workspace = new S3.WorkspaceDialogs();
        var store = new RecoveryStore(new RecoveryStoreOptions(root, true));
        string firstId, secondId;
        var form = S3.NewForm(dialogs, workspace, root);
        try
        {
            form.ActiveTab.Editor.Text = "EVALUATE ROW(\"draft\", 1)";
            S3.Check(!form.RecoveryEnabled && !form.SaveRecoveryNow() && !Directory.Exists(root), "recovery is off by default and touches no disk");
            S3.Check(!form.SetRecoveryOptIn(true) && !form.RecoveryEnabled && !File.Exists(marker), "declined disclosure keeps recovery off");
            S3.Check(workspace.Disclosure.Contains(root) && workspace.Disclosure.Contains("never uploaded"), "disclosure names the folder and privacy");
            store.Save("script0001", "csharp", "Script.csx", "// unrelated script copy", DateTime.UtcNow);
            workspace.OptIn = true;
            S3.Check(form.SetRecoveryOptIn(true) && form.RecoveryEnabled && File.Exists(marker), "accepted disclosure turns recovery on");
            var second = form.NewTab("EVALUATE ROW(\"second\", 2)");
            S3.Check(form.SaveRecoveryNow(), "recovery copies written");
            firstId = form.Tabs[0].RecoveryId; secondId = second.RecoveryId;
            var copies = store.List().Where(e => e.Kind == "dax").ToArray();
            S3.Check(copies.Length == 2 && copies.All(e => e.IsIntact) && copies.Any(e => e.DocumentId == firstId) && store.Read(secondId) == "EVALUATE ROW(\"second\", 2)", "one dax copy per dirty tab");
            dialogs.Save = Path.Combine(folder, "saved-second.dax");
            S3.Check((bool)S3.Invoke(form, "SaveDocument", false) && store.List().All(e => e.DocumentId != secondId), "saving a tab deletes its copy");
            second.Editor.Text += "\n// edited after save";
            form.SaveRecoveryNow();
            S3.Check(store.List().Any(e => e.DocumentId == secondId), "a saved tab edited again gets a new copy");
            dialogs.Answers.Enqueue(DialogResult.No);
            S3.Check(form.CloseTab(second) && store.List().All(e => e.DocumentId != secondId), "Don't save + close deletes the copy");
        }
        finally
        {
            // Simulated crash: the form goes away without FormClosing, copies stay.
            form.Dispose();
        }
        S3.Check(store.List().Any(e => e.DocumentId == firstId && e.Kind == "dax"), "copies survive a crash");

        // Damaged copy is never offered.
        store.Save("damaged0001", "dax", "damaged.dax", "EVALUATE 1", DateTime.UtcNow);
        var damaged = Directory.GetFiles(root, "damaged0001.*" + RecoveryStore.ContentExtension).Single();
        File.WriteAllText(damaged, "EVALUATE 2");

        using (var reopened = S3.NewForm(dialogs, workspace, root))
        {
            S3.Check(reopened.RecoveryEnabled, "opt-in remembered by the marker file");
            workspace.Restore = false;
            S3.Check(reopened.OfferRecovery() == 0 && store.List().Any(e => e.DocumentId == firstId) && reopened.Tabs.Count == 1, "declined restore keeps copies, opens nothing");
            workspace.Restore = true;
            S3.Check(reopened.OfferRecovery() == 1, "intact copy restored (damaged one skipped): " + reopened.StatusText);
            var restored = reopened.ActiveTab;
            S3.Check(restored.Editor.Text == "EVALUATE ROW(\"draft\", 1)" && restored.Document.IsDirty && restored.Document.FilePath == null &&
                restored.RecoveryId == firstId && restored.Page.Text.Contains("recovered"), "restored as an unsaved tab bound to its copy");
            S3.Check(reopened.StatusText.Contains("hash check"), "damaged copy reported: " + reopened.StatusText);
            S3.Check(reopened.OfferRecovery() == 0, "already restored copies are not offered twice");
            dialogs.Answers.Enqueue(DialogResult.No);
            S3.Check(reopened.CloseTab(restored) && store.List().All(e => e.DocumentId != firstId), "closing a restored tab without saving deletes its copy");
            reopened.ActiveTab.Editor.Text = "EVALUATE ROW(\"x\", 9)";
            reopened.SaveRecoveryNow();
            S3.Check(reopened.DeleteRecoveryCopies() >= 2 && store.List().All(e => e.Kind != "dax"), "delete removes every dax copy (incl. damaged)");
            S3.Check(store.List().Any(e => e.Kind == "csharp"), "script copies are never touched");
            S3.Check(reopened.SetRecoveryOptIn(false) && !reopened.RecoveryEnabled && !File.Exists(marker), "opt-out removes the marker");
            reopened.ActiveTab.Editor.Text += " ";
            S3.Check(!reopened.SaveRecoveryNow() && store.List().All(e => e.Kind != "dax"), "nothing written after opt-out");
            dialogs.DefaultAnswer = DialogResult.No;
        }
        S3.Check(Directory.GetFiles(root, "*.tmp-*").Length == 0, "no temporary recovery files left");
        Console.WriteLine("S3 recovery: opt-in disclosure, dax-only copies, delete on save/close, crash survival, restore, hash-failed skip PASS");
    }
}

internal static class DaxExternalChangeSmoke
{
    internal static void Run(string folder)
    {
        var path = Path.Combine(folder, "external.dax");
        File.WriteAllText(path, "EVALUATE ROW(\"disk\", 1)");
        var dialogs = new S3.DocumentDialogs();
        var workspace = new S3.WorkspaceDialogs();
        using (var form = S3.NewForm(dialogs, workspace, Path.Combine(folder, "recovery-external")))
        {
            var tab = form.OpenInNewTab(path);
            S3.Check(form.CheckExternalChanges() == 0 && form.ExternalBannerText == "", "no banner while unchanged");

            File.WriteAllText(path, "EVALUATE ROW(\"external\", 2)");
            S3.Check(form.CheckExternalChanges() == 1 && form.ExternalBannerText.Contains("changed on disk") && tab.Page.Text.Contains("(changed on disk)"), "clean tab: change shown");
            S3.Check(tab.Editor.Text == "EVALUATE ROW(\"disk\", 1)", "nothing reloaded automatically");
            var generation = tab.Generation;
            var held = S3.BeginFormat(form);
            S3.Check(form.ReloadActiveFromDisk() && tab.Editor.Text == "EVALUATE ROW(\"external\", 2)" && tab.Generation > generation && form.ExternalBannerText == "", "reload of a clean tab");
            S3.EndFormat(form, held);
            S3.Check(tab.Editor.Text == "EVALUATE ROW(\"external\", 2)", "formatter response discarded after reload");

            tab.Editor.Text = "EVALUATE ROW(\"mine\", 3)";
            File.WriteAllText(path, "EVALUATE ROW(\"external\", 4)");
            form.CheckExternalChanges();
            S3.Check(form.ExternalBannerText.Contains("unsaved edits are kept"), "dirty tab: banner says edits are kept");
            workspace.Reload = false;
            S3.Check(!form.ReloadActiveFromDisk() && tab.Editor.Text == "EVALUATE ROW(\"mine\", 3)", "reload over edits needs confirmation");
            dialogs.Errors = 0;
            S3.Check(!(bool)S3.Invoke(form, "SaveDocument", false) && dialogs.Errors == 1 && File.ReadAllText(path).Contains("external\", 4"), "save refuses before an explicit choice");
            S3.Check(form.KeepActiveVersion() && form.ExternalBannerText == "" && tab.Document.IsDirty, "keep my version");
            S3.Check((bool)S3.Invoke(form, "SaveDocument", false) && File.ReadAllText(path) == "EVALUATE ROW(\"mine\", 3)", "explicit keep then save overwrites");

            File.Delete(path);
            form.CheckExternalChanges();
            S3.Check(form.ExternalBannerText.Contains("deleted") && tab.Page.Text.Contains("(deleted on disk)"), "deletion shown");
            S3.Check(form.KeepActiveVersion() && (bool)S3.Invoke(form, "SaveDocument", false) && File.Exists(path), "keep after deletion recreates on save");

            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                form.CheckExternalChanges();
                S3.Check(form.ExternalBannerText.Contains("cannot be read"), "locked file reported");
                S3.Check(form.KeepActiveVersion() && form.ExternalBannerText == "" && tab.Editor.Text == "EVALUATE ROW(\"mine\", 3)", "dismiss keeps the buffer");
            }

            // Background tab: marked in its title, banner appears when it becomes active.
            var other = form.NewTab("EVALUATE 1");
            File.WriteAllText(path, "EVALUATE ROW(\"later\", 5)");
            S3.Check(form.CheckExternalChanges() == 1 && tab.Page.Text.Contains("(changed on disk)") && form.ExternalBannerText == "", "background tab marked, no banner for the active tab");
            form.ActivateTab(tab);
            S3.Check(form.ExternalBannerText.Contains("changed on disk"), "banner on activation");

            // Save As onto a file open in another tab is refused.
            form.ActivateTab(other);
            dialogs.Save = path;
            dialogs.Errors = 0;
            S3.Check(!(bool)S3.Invoke(form, "SaveDocument", true) && dialogs.Errors == 1 && File.ReadAllText(path).Contains("later"), "Save As onto another open tab's file refused");
            dialogs.DefaultAnswer = DialogResult.No;
        }
        Console.WriteLine("S3 external change: hash detection, banner, confirmed reload, explicit keep/recreate, lock, background tabs PASS");
    }
}

internal static class DaxStudioLauncherSmoke
{
    internal static void Run(string folder)
    {
        var settings = Path.Combine(folder, "settings");
        const string configured = @"E:\Portable\DAX Studio\DaxStudio.exe";
        try { PbiBenchDaxStudioLauncher.WriteConfiguredPath(@"C:\Windows\notepad.exe", settings, p => true); throw new Exception("S3 host: non-DaxStudio.exe accepted"); }
        catch (ArgumentException) { }
        S3.Check(PbiBenchDaxStudioLauncher.WriteConfiguredPath(configured, settings, p => true) == configured && PbiBenchDaxStudioLauncher.ReadConfiguredPath(settings) == configured, "configured path stored per user");
        var probed = new List<string>();
        var located = PbiBenchDaxStudioLauncher.Locate(settings, p => { probed.Add(p); return p.StartsWith(@"D:\", StringComparison.Ordinal); }, new[] { @"D:\Apps\DAX Studio" });
        S3.Check(probed[0] == configured && located == @"D:\Apps\DAX Studio\DaxStudio.exe", "configured path probed first, registry next");

        var plan = DaxStudioHandoff.Plan(located, new DaxStudioTarget("localhost:51234", "Sales Model"));
        var info = PbiBenchDaxStudioLauncher.BuildStartInfo(plan);
        S3.Check(!info.UseShellExecute && info.FileName == located && info.Arguments == "localhost:51234 \"Sales Model\"", "direct start with two quoted labels: " + info.Arguments);
        try { PbiBenchDaxStudioLauncher.BuildStartInfo(DaxStudioHandoff.Plan(null, null)); throw new Exception("S3 host: save plan started a process"); }
        catch (ArgumentException) { }

        var dialogs = new S3.DocumentDialogs();
        var workspace = new S3.WorkspaceDialogs();
        using (var form = S3.NewForm(dialogs, workspace, Path.Combine(folder, "recovery-handoff")))
        {
            ProcessStartInfo started = null; string copied = null;
            form.StartDaxStudio = s => { started = s; return null; };
            form.CopyQueryToClipboard = t => copied = t;
            form.ActiveTab.Editor.Text = "EVALUATE ROW(\"handoff\", 1)";

            // Draft mode (no live model) and no DAX Studio: saves and reports, starts nothing.
            form.DaxStudioLocator = () => null;
            dialogs.Save = null;
            var result = form.HandOffToDaxStudio();
            S3.Check(result.Kind == DaxStudioHandoffKind.SaveAndReport && started == null && form.StatusText.Contains("not saved") && workspace.Reports == 0, "cancelled save is reported, nothing started");
            dialogs.Save = Path.Combine(folder, "handoff.dax");
            result = form.HandOffToDaxStudio();
            S3.Check(result.Kind == DaxStudioHandoffKind.SaveAndReport && File.Exists(dialogs.Save) && workspace.ReportText.Contains("not found") && workspace.ReportText.Contains(dialogs.Save), "missing DAX Studio: saved .dax and reported");

            // Found but connection-string-like target: refused, saved, secret never shown.
            form.DaxStudioLocator = () => located;
            form.DaxStudioTargetProvider = () => new DaxStudioTarget("Data Source=localhost;Password=synthetic-secret", "db");
            var saves = Directory.GetFiles(folder, "*.dax").Length;
            result = form.HandOffToDaxStudio();
            S3.Check(result.Kind == DaxStudioHandoffKind.SaveAndReport && started == null && !workspace.ReportText.Contains("synthetic-secret") && !form.StatusText.Contains("synthetic-secret"),
                "connection string refused without echo");
            S3.Check(Directory.GetFiles(folder, "*.dax").Length == saves && dialogs.Errors == 0, "clean saved document is not saved again");

            // Live labels: launch, clipboard gets only the query (selection wins).
            form.DaxStudioTargetProvider = () => new DaxStudioTarget("localhost:51234", "Sales Model");
            var editor = form.ActiveTab.Editor;
            editor.Select(0, "EVALUATE".Length);
            result = form.HandOffToDaxStudio();
            S3.Check(result.Kind == DaxStudioHandoffKind.LaunchConnected && started != null && started.Arguments == "localhost:51234 \"Sales Model\"" &&
                !started.UseShellExecute && copied == "EVALUATE" && form.StatusText.Contains("clipboard"), "connected launch: " + form.StatusText);

            // Start failure falls back to save-and-report once (no retry).
            var attempts = 0;
            form.StartDaxStudio = s => { attempts++; return "DAX Studio could not be started from " + s.FileName + "."; };
            editor.Text += "\n// changed";
            result = form.HandOffToDaxStudio();
            S3.Check(attempts == 1 && workspace.ReportText.Contains("could not be started") && !form.ActiveTab.Document.IsDirty, "start failure saved and reported once");

            // Default provider: no live TE2 connection behind the workbench means draft mode.
            form.DaxStudioTargetProvider = null;
            form.StartDaxStudio = s => { attempts++; return null; };
            result = form.HandOffToDaxStudio();
            S3.Check(result.Kind == DaxStudioHandoffKind.SaveAndReport && attempts == 1 && workspace.ReportText.Contains("No live model connection"), "draft mode never launches");
        }
        Console.WriteLine("S3 DAX Studio launcher: configured/registry lookup, direct start args, labels only, save-and-report fallbacks PASS");
    }
}
