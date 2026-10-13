using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PbiBench.Core.Automation;
using PbiBench.Core.Documents;
using TabularEditor.TOMWrapper;
using TabularEditor.UI;

namespace TabularEditor.PbiBench.Automation
{
    /// <summary>
    /// AUTO-001 script workspace: several C# script tabs, a Problems list fed by TE2's own compiler, explicit
    /// per-version trust confirmation ("runs with your permissions; not sandboxed") and execution through TE2's
    /// ScriptEngine in one undo batch. Optional, disclosed local recovery copies through <see cref="RecoveryStore"/>.
    /// </summary>
    internal sealed class PbiBenchScriptWorkspaceForm : Form
    {
        internal const string RecoveryKind = "csharp";
        private readonly Func<TabularModelHandler> _currentHandler;
        private readonly Func<UITreeSelection> _selection;
        private readonly TabularModelHandler _openedHandler;
        private readonly string _recoveryRoot;
        private readonly TabControl _tabs = new TabControl { Dock = DockStyle.Fill };
        private readonly ListView _problems = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 48, AutoEllipsis = true };
        private readonly CheckBox _rollback = new CheckBox { Text = "Roll back model changes if the script fails", AutoSize = true, Checked = true };
        private readonly CheckBox _recoveryOptIn = new CheckBox { Text = "Keep local recovery copies", AutoSize = true };
        private readonly Timer _recoveryTimer = new Timer { Interval = 3000 };
        private readonly Dictionary<string, PbiBenchScriptCompilation> _compiled = new Dictionary<string, PbiBenchScriptCompilation>();
        private bool _loading;

        internal PbiBenchScriptWorkspaceForm(Func<TabularModelHandler> currentHandler, Func<UITreeSelection> selection, string recoveryRoot = null)
        {
            _currentHandler = currentHandler ?? throw new ArgumentNullException(nameof(currentHandler));
            _selection = selection ?? (() => null);
            _openedHandler = currentHandler();
            _recoveryRoot = recoveryRoot ?? RecoveryStoreOptions.DefaultRoot;
            Text = "TE2 Enhanced · C# script workspace (trusted code, not sandboxed)";
            Size = new Size(1000, 700); MinimumSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true;

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            toolbar.Controls.AddRange(new Control[]
            {
                MakeButton("New", (s, e) => NewDocument("")),
                MakeButton("Open…", (s, e) => OpenFile()),
                MakeButton("Save", (s, e) => SaveActive(false)),
                MakeButton("Save as…", (s, e) => SaveActive(true)),
                MakeButton("Close tab", (s, e) => CloseActive()),
                MakeButton("Compile (F6)", (s, e) => ShowCompile(CompileActive())),
                MakeButton("Run… (F5)", (s, e) => RunActive(ConfirmTrustDialog)),
                _rollback, _recoveryOptIn,
                MakeButton("Delete script recovery copies", (s, e) => DeleteRecoveryCopies())
            });
            _problems.Columns.Add("Severity", 80); _problems.Columns.Add("Location", 110); _problems.Columns.Add("Code", 70); _problems.Columns.Add("Message", 600);
            _problems.DoubleClick += (s, e) => GoToProblem();
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Panel1MinSize = 120, Panel2MinSize = 80 };
            split.Panel1.Controls.Add(_tabs);
            split.Panel2.Controls.Add(_problems);
            _status.Text = "Scripts run inside TE2 with your Windows permissions and are not sandboxed. Model changes form one TE2 undo step; nothing is saved to disk or deployed.";
            Controls.Add(split); Controls.Add(toolbar); Controls.Add(_status);
            Load += (s, e) => { if (split.Height > split.Panel1MinSize + split.Panel2MinSize + split.SplitterWidth) split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Height * 2 / 3); };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F6) { ShowCompile(CompileActive()); e.Handled = true; }
                else if (e.KeyCode == Keys.F5) { RunActive(ConfirmTrustDialog); e.Handled = true; }
            };
            _tabs.SelectedIndexChanged += (s, e) => ShowProblems(ActiveDocument);

            _loading = true;
            _recoveryOptIn.Checked = File.Exists(OptInMarkerPath);
            _loading = false;
            _recoveryOptIn.CheckedChanged += (s, e) => SetRecoveryOptIn(_recoveryOptIn.Checked);
            _recoveryTimer.Tick += (s, e) => { _recoveryTimer.Stop(); SaveRecoveryNow(); };
            FormClosing += OnClosingWorkspace;
            Disposed += (s, e) =>
            {
                _recoveryTimer.Dispose();
                // Copies kept at close become recoverable for the next workspace of this process; other instances never see them as orphans while TE2 runs.
                try { var store = OwnStore(false); foreach (var doc in Workspace.Documents) store.Release(doc.Id); } catch (Exception) { }
            };

            const string starter = "// C# script. Model and Selected are available, as in TE2 Advanced Scripting.\r\n";
            // The recovery prompt needs a visible owner, so it waits for Load; without opt-in nothing is read from disk.
            if (_recoveryOptIn.Checked) Load += (s, e) => { if (!OfferRecovery()) NewStarter(starter); };
            else NewStarter(starter);
        }

        internal ScriptWorkspace Workspace { get; } = new ScriptWorkspace();
        internal ScriptDocument ActiveDocument => _tabs.SelectedTab?.Tag as ScriptDocument;
        internal ListView Problems => _problems;
        internal string StatusText => _status.Text;
        internal RecoveryStore Recovery => OwnStore(_recoveryOptIn.Checked);
        /// <summary>Liveness probe for copy owners (null = real process check); smokes model other TE2 instances with it.</summary>
        internal Func<RecoveryOwner, bool> RecoveryOwnerProbe { get; set; }
        private RecoveryStore OwnStore(bool enabled) => new RecoveryStore(new RecoveryStoreOptions(_recoveryRoot, enabled), null, RecoveryOwnerProbe);
        private string OptInMarkerPath => _recoveryRoot.TrimEnd(Path.DirectorySeparatorChar) + ".optin";

        internal ScriptDocument NewDocument(string text) => AddTab(Workspace.New(text));

        // The untouched starter comment is not "unsaved work": closing without edits must not prompt.
        private void NewStarter(string text)
        {
            var doc = Workspace.New(text);
            doc.MarkSaved(null);
            AddTab(doc);
        }

        internal void SetActiveText(string text)
        {
            var editor = _tabs.SelectedTab?.Controls.OfType<TextBox>().FirstOrDefault();
            if (editor != null) editor.Text = text; // TextChanged updates the document
        }

        internal PbiBenchScriptCompilation CompileActive()
        {
            var doc = ActiveDocument;
            if (doc == null) return null;
            PbiBenchScriptCompilation result;
            try { result = PbiBenchScriptCompiler.Compile(doc); }
            catch (Exception ex) { SetStatus("Compiler unavailable: " + ex.Message); return null; }
            _compiled[doc.Id] = result;
            ShowProblems(doc);
            return result;
        }

        /// <summary>Compile (if needed) → refuse on errors → trust for this exact version → stale model check → run in one undo batch.</summary>
        internal PbiBenchScriptRunOutcome RunActive(Func<ScriptDocument, IReadOnlyList<ScriptDiagnostic>, bool> confirmTrust)
        {
            var doc = ActiveDocument;
            if (doc == null) return null;
            var handler = _currentHandler();
            if (handler?.Model == null || !ReferenceEquals(handler, _openedHandler)) { SetStatus("The loaded model changed since this workspace opened. Reopen it to run scripts."); return null; }
            if (!_compiled.TryGetValue(doc.Id, out var compilation) || !compilation.IsFor(doc)) compilation = CompileActive();
            if (compilation == null) return null;
            if (!compilation.Succeeded) { SetStatus("Not run: " + ScriptDiagnostics.Summary(compilation.Diagnostics) + ". Fix the Problems list first."); return null; }
            if (!doc.IsTrusted)
            {
                var reviewed = doc.ContentHash;
                if (confirmTrust == null || !confirmTrust(doc, ScriptDiagnostics.Advisory(doc))) { SetStatus("Not run: trust was not confirmed."); return null; }
                try { doc.ConfirmTrust(reviewed); }
                catch (InvalidOperationException ex) { SetStatus(ex.Message); return null; }
                if (_tabs.SelectedTab != null) _tabs.SelectedTab.Text = TabTitle(doc);
            }
            PbiBenchScriptRunOutcome outcome;
            try { outcome = PbiBenchScriptCompiler.Run(handler, doc, compilation, _selection(), _rollback.Checked); }
            catch (InvalidOperationException ex) { SetStatus("Not run: " + ex.Message); return null; }
            switch (outcome.State)
            {
                case PbiBenchScriptRunState.Succeeded:
                    SetStatus("Script finished: " + outcome.ModelChanges + " model change(s) in one TE2 undo step. Nothing was saved to disk or deployed."); break;
                case PbiBenchScriptRunState.Cancelled:
                    SetStatus("Script cancelled. " + (outcome.RolledBack ? "Model changes were rolled back." : outcome.ModelChanges + " model change(s) kept; use TE2 Undo.")); break;
                default:
                    SetStatus("Script failed" + (outcome.Line > 0 ? " at line " + outcome.Line : "") + ": " + outcome.Message + " " +
                        (outcome.RolledBack ? "Model changes were rolled back." : "Partial model changes were kept; use TE2 Undo.") +
                        " Effects outside the model (files, network) cannot be undone.");
                    break;
            }
            return outcome;
        }

        internal bool SaveRecoveryNow()
        {
            if (!_recoveryOptIn.Checked) return false;
            try
            {
                var store = Recovery;
                foreach (var doc in Workspace.Documents.Where(d => d.IsDirty))
                    store.Save(doc.Id, RecoveryKind, doc.Title, doc.Text, DateTime.UtcNow);
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { SetStatus("Recovery copy not written: " + ex.Message); return false; }
        }

        /// <summary>Restore prompt (count → yes/no); replaceable by smokes. Default asks with a message box.</summary>
        internal Func<int, bool> RestorePrompt { get; set; }

        internal bool OfferRecovery()
        {
            if (!_recoveryOptIn.Checked) return false;
            IReadOnlyList<RecoveryEntry> entries;
            // Only orphaned copies: another running TE2 instance's live drafts are never offered (multi-instance safety).
            try { entries = Recovery.ListRecoverable().Where(e => e.Kind == RecoveryKind && e.IsIntact).Take(ScriptWorkspace.MaxDocuments).ToArray(); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return false; }
            if (entries.Count == 0) return false;
            var yes = RestorePrompt != null ? RestorePrompt(entries.Count) :
                MessageBox.Show(this, entries.Count + " unsaved script(s) were found in local recovery (" + _recoveryRoot + ").\r\n\r\nRestore them as tabs? " +
                "Restored scripts are untrusted until you confirm them again.", "Script recovery", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
            if (!yes) return false;
            var restored = 0;
            foreach (var entry in entries)
            {
                try
                {
                    // New id: the copy is rewritten under it (owned by this process) before the source copy is removed.
                    var newId = Guid.NewGuid().ToString("N");
                    Recovery.Adopt(entry.DocumentId, newId, DateTime.UtcNow, out var text);
                    AddTab(Workspace.Add(new ScriptDocument(newId, entry.Title.EndsWith(" (recovered)") ? entry.Title : entry.Title + " (recovered)", text)));
                    restored++;
                }
                catch (InvalidDataException) { SetStatus("A recovery copy failed its hash check and was not restored."); }
                catch (InvalidOperationException) { } // claimed meanwhile by a running instance
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SetStatus("A recovery copy could not be restored: " + ex.Message); }
            }
            return restored > 0;
        }

        private void SetRecoveryOptIn(bool enabled)
        {
            if (_loading) return;
            try
            {
                if (enabled)
                {
                    var store = OwnStore(true);
                    if (MessageBox.Show(this, store.Disclosure + "\r\n\r\nKeep recovery copies of unsaved scripts?", "Local recovery copies",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
                    { _loading = true; _recoveryOptIn.Checked = false; _loading = false; return; }
                    Directory.CreateDirectory(Path.GetDirectoryName(OptInMarkerPath));
                    if (!File.Exists(OptInMarkerPath)) File.WriteAllText(OptInMarkerPath, "TE2 Enhanced: script recovery copies enabled by the user.");
                    SaveRecoveryNow();
                }
                else if (File.Exists(OptInMarkerPath)) File.Delete(OptInMarkerPath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SetStatus("Recovery setting not changed: " + ex.Message); }
        }

        internal int DeleteRecoveryCopies()
        {
            try
            {
                // Only script copies (the store is shared with DAX documents), and never a running TE2 instance's live drafts.
                var store = OwnStore(true);
                var listed = store.List().Where(e => e.Kind == RecoveryKind && !e.IsHeldByOtherInstance).Select(e => e.DocumentId).ToArray();
                var own = Workspace.Documents.Select(d => d.Id).Where(id => !listed.Contains(id)).Distinct().ToArray();
                var count = listed.Count(store.Delete) + own.Count(store.DeleteOwn);
                SetStatus("Deleted " + count + " script recovery cop" + (count == 1 ? "y" : "ies") + " from " + _recoveryRoot + ".");
                return count;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SetStatus("Recovery copies not deleted: " + ex.Message); return 0; }
        }

        private ScriptDocument AddTab(ScriptDocument doc)
        {
            var editor = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, AcceptsTab = true, AcceptsReturn = true, WordWrap = false,
                ScrollBars = ScrollBars.Both, Font = new Font(FontFamily.GenericMonospace, 10f), MaxLength = ScriptDocument.MaxChars, Text = doc.Text
            };
            var page = new TabPage(TabTitle(doc)) { Tag = doc };
            editor.TextChanged += (s, e) =>
            {
                try { doc.SetText(editor.Text); }
                catch (ArgumentException ex) { SetStatus(ex.Message); return; }
                page.Text = TabTitle(doc);
                if (_compiled.TryGetValue(doc.Id, out var c) && !c.IsFor(doc)) MarkProblemsStale(doc);
                if (_recoveryOptIn.Checked) { _recoveryTimer.Stop(); _recoveryTimer.Start(); }
            };
            page.Controls.Add(editor);
            _tabs.TabPages.Add(page);
            _tabs.SelectedTab = page;
            ShowProblems(doc);
            return doc;
        }

        private static string TabTitle(ScriptDocument doc) => doc.Title + (doc.IsDirty ? " *" : "") + (doc.IsTrusted ? " (trusted)" : "");

        private void ShowCompile(PbiBenchScriptCompilation result)
        {
            if (result == null) return;
            SetStatus(result.Succeeded ? "Compiled: " + ScriptDiagnostics.Summary(result.Diagnostics) + ". Run asks for trust before executing."
                : "Compile failed: " + ScriptDiagnostics.Summary(result.Diagnostics) + ".");
        }

        private void ShowProblems(ScriptDocument doc)
        {
            _problems.BeginUpdate();
            _problems.Items.Clear();
            if (doc != null && _compiled.TryGetValue(doc.Id, out var result))
            {
                var stale = !result.IsFor(doc);
                foreach (var d in result.Diagnostics)
                {
                    var item = new ListViewItem((stale ? "(stale) " : "") + d.Severity) { Tag = d };
                    item.SubItems.Add(d.Location); item.SubItems.Add(d.Code); item.SubItems.Add(d.Message);
                    _problems.Items.Add(item);
                }
            }
            _problems.EndUpdate();
        }

        private void MarkProblemsStale(ScriptDocument doc)
        {
            if (ActiveDocument == doc) ShowProblems(doc);
        }

        private void GoToProblem()
        {
            if (!(_problems.SelectedItems.Count == 1 && _problems.SelectedItems[0].Tag is ScriptDiagnostic d) || d.Line <= 0) return;
            var editor = _tabs.SelectedTab?.Controls.OfType<TextBox>().FirstOrDefault();
            if (editor == null || d.Line > editor.Lines.Length) return;
            var index = editor.GetFirstCharIndexFromLine(d.Line - 1);
            if (index < 0) return;
            editor.SelectionStart = Math.Min(editor.TextLength, index + Math.Max(0, d.Column - 1));
            editor.SelectionLength = 0;
            editor.ScrollToCaret();
            editor.Focus();
        }

        private void OpenFile()
        {
            using (var dialog = new OpenFileDialog { Filter = "C# script (*.csx;*.cs)|*.csx;*.cs|All files (*.*)|*.*", Title = "Open C# script" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var info = new FileInfo(dialog.FileName);
                    if (info.Length > ScriptDocument.MaxChars * 3L) { SetStatus("File is too large for the script workspace (1 MiB of text)."); return; }
                    var text = File.ReadAllText(dialog.FileName);
                    AddTab(Workspace.Add(new ScriptDocument(Guid.NewGuid().ToString("N"), Path.GetFileName(dialog.FileName), text, dialog.FileName, isSaved: true)));
                    SetStatus("Opened " + dialog.FileName + ". Opened scripts are untrusted until you confirm them at Run.");
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
                { SetStatus("Cannot open: " + ex.Message); }
            }
        }

        /// <summary>Plain user-initiated file save (not a model save). A failure is reported, never retried.</summary>
        private bool SaveActive(bool saveAs)
        {
            var doc = ActiveDocument;
            if (doc == null) return false;
            var path = doc.FilePath;
            if (saveAs || string.IsNullOrEmpty(path))
            {
                using (var dialog = new SaveFileDialog { Filter = "C# script (*.csx)|*.csx|C# file (*.cs)|*.cs", FileName = doc.Title + ".csx", Title = "Save C# script" })
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                    path = dialog.FileName;
                }
            }
            try
            {
                File.WriteAllText(path, doc.Text, new UTF8Encoding(false));
                doc.MarkSaved(path);
                doc.Rename(Path.GetFileName(path));
                if (_tabs.SelectedTab != null) _tabs.SelectedTab.Text = TabTitle(doc);
                try { OwnStore(false).DeleteOwn(doc.Id); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
                SetStatus("Saved script to " + path + ".");
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SetStatus("Script not saved: " + ex.Message); return false; }
        }

        private void CloseActive()
        {
            var doc = ActiveDocument;
            if (doc == null) return;
            if (doc.IsDirty && MessageBox.Show(this, "Discard unsaved changes to '" + doc.Title + "'?", "Close script", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            Workspace.Close(doc.Id, true);
            _compiled.Remove(doc.Id);
            try { OwnStore(false).DeleteOwn(doc.Id); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            _tabs.TabPages.Remove(_tabs.SelectedTab);
            ShowProblems(ActiveDocument);
        }

        private void OnClosingWorkspace(object sender, FormClosingEventArgs e)
        {
            var dirty = Workspace.Documents.Where(d => d.IsDirty).ToArray();
            if (dirty.Length == 0 || e.CloseReason != CloseReason.UserClosing) { SaveRecoveryNow(); return; }
            var keep = _recoveryOptIn.Checked ? " Recovery copies are kept locally." : "";
            if (MessageBox.Show(this, dirty.Length + " script(s) have unsaved changes." + keep + " Close the workspace anyway?", "Close script workspace",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) { e.Cancel = true; return; }
            SaveRecoveryNow();
        }

        private bool ConfirmTrustDialog(ScriptDocument doc, IReadOnlyList<ScriptDiagnostic> advisory)
        {
            using (var dialog = new Form { Text = "Run trusted C# script?", Size = new Size(640, 420), MinimumSize = new Size(480, 320),
                StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi, MinimizeBox = false, MaximizeBox = false })
            {
                var details = new StringBuilder();
                details.AppendLine(ScriptDocument.TrustStatement).AppendLine();
                details.AppendLine("Script: " + doc.Title + "  (SHA-256 " + doc.ContentHash.Substring(0, 12) + "…, " + doc.Text.Length + " chars)");
                details.AppendLine("Trust applies to this exact text only; any edit asks again.").AppendLine();
                if (advisory.Count > 0)
                {
                    details.AppendLine("Advisory keyword scan (not a security check, not exhaustive):");
                    foreach (var a in advisory) details.AppendLine("  • " + a.Location + ": " + a.Message);
                }
                else details.AppendLine("The advisory keyword scan found nothing; that does NOT mean the script is safe.");
                var text = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = details.ToString() };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                var run = new Button { Text = "Run with my permissions", DialogResult = DialogResult.Yes, AutoSize = true };
                buttons.Controls.Add(cancel); buttons.Controls.Add(run);
                dialog.Controls.Add(text); dialog.Controls.Add(buttons);
                dialog.AcceptButton = cancel; dialog.CancelButton = cancel; // Enter does not run code by accident
                return dialog.ShowDialog(this) == DialogResult.Yes;
            }
        }

        private void SetStatus(string text) => _status.Text = text;

        private static Button MakeButton(string text, EventHandler click)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += click;
            return button;
        }
    }
}
