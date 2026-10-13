using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using PbiBench.Core.Documents;

namespace TabularEditor.PbiBench.Dax
{
    /// <summary>
    /// DAX-013 multiple documents. Each tab owns its <see cref="DaxDocument"/> (stable id) and its own editor,
    /// so undo, caret and results stay per tab. Guards keep the S001 rules per tab: a tab's Generation and
    /// TextRevision are stamps drawn from global monotonic counters, so they are unique across tabs and any
    /// asynchronous response (formatter) or completion captured for one tab is rejected for every other tab,
    /// after a replacement, after an edit, or after the tab was closed (responses are keyed by document id).
    /// External changes are detected by content hash on activation (no watcher); nothing is reloaded or
    /// overwritten without an explicit choice.
    /// </summary>
    internal sealed partial class PbiBenchDaxWorkbenchForm
    {
        internal sealed class WorkbenchTab
        {
            internal WorkbenchTab(DaxDocument document) { Document = document; RecoveryId = document.Id; }
            internal DaxDocument Document { get; }
            internal RichTextBox Editor { get; set; }
            internal TabPage Page { get; set; }
            /// <summary>Stamp of the last content replacement (New/Open/history/reload).</summary>
            internal long Generation { get; set; }
            /// <summary>Stamp of the last text change in this tab's editor.</summary>
            internal long TextRevision { get; set; }
            internal DaxQueryResult Execution { get; set; }
            internal DaxDiskState External { get; set; } = DaxDiskState.Unchanged;
            internal string TitleSuffix { get; set; } = "";
            /// <summary>Recovery copy id: the document id, or the id of the copy this tab was restored from.</summary>
            internal string RecoveryId { get; set; }
            internal bool HasRecoveryCopy { get; set; }
        }

        private readonly DocumentSet<DaxDocument> _documents = new DocumentSet<DaxDocument>();
        private readonly Dictionary<string, WorkbenchTab> _tabStates = new Dictionary<string, WorkbenchTab>(StringComparer.Ordinal);
        private TabControl _documentTabs;
        private Panel _externalBanner;
        private Label _externalText;
        private Button _externalReload, _externalKeep;
        private ToolStripItem[] _tabCommands = Array.Empty<ToolStripItem>();
        private bool _selectingTab;

        internal WorkbenchTab ActiveTab => _documents.Active == null ? null : _tabStates[_documents.Active.Id];
        internal IReadOnlyList<WorkbenchTab> Tabs => _documents.Documents.Select(d => _tabStates[d.Id]).ToArray();
        internal WorkbenchTab FindTab(string documentId) =>
            documentId != null && _tabStates.TryGetValue(documentId, out var tab) && _documents.FindById(documentId) != null ? tab : null;
        internal string StatusText => _status.Text;
        internal string ExternalBannerText => _externalBannerShown ? _externalText.Text : "";
        private bool _externalBannerShown;

        private void InitializeTabs(Control host, string initialText)
        {
            _documentTabs = new TabControl { Dock = DockStyle.Fill, ShowToolTips = true, HotTrack = true };
            _documentTabs.Selecting += (s, e) =>
            {
                if (_runCts == null || _selectingTab) return;
                e.Cancel = true;
                SetStatus("Tabs stay on the running query until it finishes or is cancelled.");
            };
            _documentTabs.SelectedIndexChanged += (s, e) =>
            {
                if (_selectingTab) return;
                var id = (_documentTabs.SelectedTab?.Tag as WorkbenchTab)?.Document.Id;
                if (id != null && !ReferenceEquals(_documents.Active, _documents.FindById(id))) ActivateTab(_tabStates[id]);
            };
            _documentTabs.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Middle) return;
                for (var i = 0; i < _documentTabs.TabCount; i++)
                    if (_documentTabs.GetTabRect(i).Contains(e.Location)) { CloseTab(_documentTabs.TabPages[i].Tag as WorkbenchTab); return; }
            };

            _externalBanner = new Panel { Dock = DockStyle.Top, Height = 34, Visible = false, BackColor = Color.FromArgb(255, 244, 206), Padding = new Padding(6, 4, 6, 4) };
            _externalText = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.Black };
            _externalReload = new Button { Text = "Reload from disk", AutoSize = true, Dock = DockStyle.Right, UseVisualStyleBackColor = true };
            _externalKeep = new Button { Text = "Keep my version", AutoSize = true, Dock = DockStyle.Right, UseVisualStyleBackColor = true };
            _externalReload.Click += (s, e) => ReloadActiveFromDisk();
            _externalKeep.Click += (s, e) => KeepActiveVersion();
            _externalBanner.Controls.Add(_externalText);
            _externalBanner.Controls.Add(_externalKeep);
            _externalBanner.Controls.Add(_externalReload);

            host.Controls.Add(_documentTabs);
            host.Controls.Add(_externalBanner);

            var first = new DaxDocument();
            first.New(initialText);
            AddTab(first, true);
            Activated += (s, e) => CheckExternalChanges();
        }

        private ToolStripItem CreateOpenInCurrentTabCommand()
        {
            var item = new ToolStripMenuItem("Open in &current tab…");
            item.Click += (s, e) => OpenDocument();
            return item;
        }

        private ToolStripItem CreateSaveAllCommand()
        {
            var item = new ToolStripMenuItem("Save A&ll") { ShortcutKeys = Keys.Control | Keys.Alt | Keys.S };
            item.Click += (s, e) => SaveAll();
            return item;
        }

        private ToolStripItem[] CreateTabCommands()
        {
            var close = new ToolStripMenuItem("&Close tab") { ShortcutKeys = Keys.Control | Keys.W, ShortcutKeyDisplayString = "Ctrl+W / Ctrl+F4" };
            close.Click += (s, e) => CloseTab(ActiveTab);
            var closeAll = new ToolStripMenuItem("Close a&ll tabs");
            closeAll.Click += (s, e) => CloseAllTabs();
            var check = new ToolStripMenuItem("Check files for external changes");
            check.Click += (s, e) => { var n = CheckExternalChanges(); if (n == 0) SetStatus("No open file changed on disk."); };
            var next = new ToolStripMenuItem("Next tab") { ShortcutKeyDisplayString = "Ctrl+Tab" };
            next.Click += (s, e) => SelectNeighbourTab(1);
            var previous = new ToolStripMenuItem("Previous tab") { ShortcutKeyDisplayString = "Ctrl+Shift+Tab" };
            previous.Click += (s, e) => SelectNeighbourTab(-1);
            _tabCommands = new ToolStripItem[] { close, closeAll, new ToolStripSeparator(), next, previous, check };
            return _tabCommands;
        }

        private void SetTabCommandsEnabled(bool enabled)
        {
            foreach (var item in _tabCommands) item.Enabled = enabled;
        }

        private RichTextBox CreateEditor(WorkbenchTab tab)
        {
            var editor = new RichTextBox
            {
                Dock = DockStyle.Fill,
                AcceptsTab = !TabMovesFocus,
                DetectUrls = false,
                Font = new Font(FontFamily.GenericMonospace, 10.0f),
                WordWrap = false,
                BorderStyle = BorderStyle.None
            };
            editor.TextChanged += (s, e) =>
            {
                tab.Document.Text = editor.Text;
                _textRevision++;
                tab.TextRevision = _textRevision;
                UpdateTabTitle(tab);
                if (ReferenceEquals(tab, ActiveTab)) UpdateDocumentTitle();
                ScheduleRecovery();
            };
            editor.SelectionChanged += (s, e) =>
            {
                if (ReferenceEquals(editor, _queryEditor))
                    _executeButton.Text = editor.SelectionLength > 0 ? "Execute selection" : "Execute";
            };
            return editor;
        }

        /// <summary>Adds a tab for <paramref name="document"/> (content already loaded). Returns null when the set is full.</summary>
        internal WorkbenchTab AddTab(DaxDocument document, bool activate, string titleSuffix = "")
        {
            if (!_documents.CanAdd)
            {
                SetStatus("At most " + _documents.MaxDocuments + " DAX tabs can be open. Close a tab first.");
                return null;
            }
            var tab = new WorkbenchTab(document) { TitleSuffix = titleSuffix ?? "" };
            tab.Editor = CreateEditor(tab);
            tab.Page = new TabPage { Tag = tab, Padding = new Padding(0), UseVisualStyleBackColor = true };
            tab.Page.Controls.Add(tab.Editor);
            _tabStates[document.Id] = tab;
            _documents.Add(document, activate: false);
            _documentGeneration++;
            tab.Generation = _documentGeneration;
            if (IsHandleCreated) EnsureEditorHandle(tab);
            tab.Editor.Text = document.Text;
            tab.Editor.ClearUndo();
            _selectingTab = true;
            try { _documentTabs.TabPages.Add(tab.Page); }
            finally { _selectingTab = false; }
            UpdateTabTitle(tab);
            if (activate || _documents.Active == null) ActivateTab(tab);
            else UpdateDocumentTitle();
            return tab;
        }

        /// <summary>
        /// A RichTextBox raises TextChanged only once its native handle exists, and a TabControl creates the
        /// controls of its pages lazily. Every tab editor therefore gets its handle as soon as the form has one,
        /// so document text, dirty state and the per-tab stamps never miss an edit on a background tab.
        /// </summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            foreach (var tab in Tabs) EnsureEditorHandle(tab);
        }

        private static void EnsureEditorHandle(WorkbenchTab tab)
        {
            if (tab.Editor != null && !tab.Editor.IsHandleCreated && !tab.Editor.IsDisposed) { var handle = tab.Editor.Handle; }
        }

        /// <summary>Makes <paramref name="tab"/> the active document: editor, document, results and external-change banner follow.</summary>
        internal bool ActivateTab(WorkbenchTab tab)
        {
            if (tab == null || _documents.FindById(tab.Document.Id) == null) return false;
            if (_runCts != null && !ReferenceEquals(tab, ActiveTab)) { SetStatus("Tabs stay on the running query until it finishes or is cancelled."); return false; }
            _documents.Activate(tab.Document.Id);
            _document = tab.Document;
            _queryEditor = tab.Editor;
            _selectingTab = true;
            try { if (!ReferenceEquals(_documentTabs.SelectedTab, tab.Page)) _documentTabs.SelectedTab = tab.Page; }
            finally { _selectingTab = false; }
            ResetAdvisoryState();
            if (_results != null)
            {
                BindExecutionResult(tab.Execution ?? new DaxQueryResult());
                _executeButton.Text = _queryEditor.SelectionLength > 0 ? "Execute selection" : "Execute";
            }
            CheckExternalChange(tab);
            UpdateDocumentTitle();
            if (Visible) _queryEditor.Focus();
            return true;
        }

        private void SelectNeighbourTab(int offset)
        {
            if (_documents.Count < 2 || _runCts != null) return;
            var target = _documents.Neighbour(offset);
            if (target != null) ActivateTab(_tabStates[target.Id]);
        }

        private void UpdateTabTitle(WorkbenchTab tab)
        {
            if (tab?.Page == null) return;
            var external = tab.External == DaxDiskState.Changed ? " (changed on disk)" : tab.External == DaxDiskState.Missing ? " (deleted on disk)" : "";
            var text = tab.Document.Title + tab.TitleSuffix + (tab.Document.IsDirty ? " *" : "") + external;
            if (tab.Page.Text != text) tab.Page.Text = text;
            tab.Page.ToolTipText = tab.Document.FilePath ?? "Unsaved draft (not on disk)";
        }

        /// <summary>Opens a new empty tab (or one with <paramref name="text"/> as an unsaved draft).</summary>
        internal WorkbenchTab NewTab(string text = null)
        {
            if (_runCts != null || _closing) return null;
            var document = new DaxDocument();
            document.New(text ?? "", unsaved: !string.IsNullOrEmpty(text));
            var tab = AddTab(document, true);
            if (tab != null) SetStatus("New DAX tab · Ctrl+Tab switches · Ctrl+W closes.");
            return tab;
        }

        /// <summary>Opens a file in a new tab. An already open file is activated; an untouched empty draft tab is reused.</summary>
        internal WorkbenchTab OpenInNewTab(string path = null)
        {
            if (_runCts != null || _closing) return null;
            path = path ?? _documentDialogs.OpenPath(this);
            if (path == null) return null;
            if (ActivateIfOpenElsewhere(path, includeActive: true)) return ActiveTab;
            var document = new DaxDocument();
            if (!DocumentOperation(() => document.Open(path))) return null;
            var active = ActiveTab;
            if (active != null && active.Document.FilePath == null && !active.Document.IsDirty && active.Document.Text.Trim().Length == 0)
            {
                // Reuse the pristine empty tab: replace in place so its guards see a content replacement.
                DocumentOperation(() => { _document.Open(path); DisplayDocument(); });
                return ActiveTab;
            }
            var tab = AddTab(document, true);
            if (tab != null) SetStatus("Opened " + document.FilePath + " in a new tab.");
            return tab;
        }

        private bool IsOpenInOtherTab(string path) => _documents.FindByPath(path, except: _documents.Active) != null;

        private bool ActivateIfOpenElsewhere(string path, bool includeActive = false)
        {
            var existing = _documents.FindByPath(path, except: includeActive ? null : _documents.Active);
            if (existing == null) return false;
            ActivateTab(_tabStates[existing.Id]);
            SetStatus(existing.Title + " is already open; switched to its tab.");
            return true;
        }

        /// <summary>Asks about one dirty tab (Save / Don't save / Cancel). The tab is activated first so the prompt matches what is visible.</summary>
        private bool ConfirmTab(WorkbenchTab tab)
        {
            if (!tab.Document.IsDirty) return true;
            if (!ReferenceEquals(tab, ActiveTab) && !ActivateTab(tab)) return false;
            return ConfirmDocumentReplacement();
        }

        /// <summary>Closes one tab after its dirty prompt. The workbench always keeps one tab: closing the last one leaves an empty draft.</summary>
        internal bool CloseTab(WorkbenchTab tab)
        {
            if (tab == null || _runCts != null || _documents.FindById(tab.Document.Id) == null) return false;
            if (!ConfirmTab(tab)) return false;
            RemoveTab(tab);
            if (_documents.Count == 0) NewTabAfterClose();
            else ActivateTab(ActiveTab);
            return true;
        }

        /// <summary>
        /// Close all: every dirty tab is asked about first (active tab, then left to right). Cancel at any
        /// prompt closes nothing (documents already saved by "Yes" stay saved). Then all tabs close and one
        /// empty draft remains.
        /// </summary>
        internal bool CloseAllTabs()
        {
            if (_runCts != null) return false;
            if (!ConfirmAllDocuments()) { SetStatus("Close all cancelled; every tab is still open."); return false; }
            foreach (var tab in Tabs) RemoveTab(tab);
            NewTabAfterClose();
            SetStatus("All DAX tabs closed.");
            return true;
        }

        /// <summary>True when every dirty document was saved or explicitly discarded; false when the user cancelled.</summary>
        private bool ConfirmAllDocuments()
        {
            foreach (var document in _documents.CloseAllOrder())
                if (document.IsDirty && !ConfirmTab(_tabStates[document.Id])) return false;
            return true;
        }

        private void NewTabAfterClose()
        {
            var document = new DaxDocument();
            document.New();
            AddTab(document, true);
        }

        private void RemoveTab(WorkbenchTab tab)
        {
            DiscardRecoveryCopy(tab);
            _documents.Remove(tab.Document.Id);
            _tabStates.Remove(tab.Document.Id);
            _selectingTab = true;
            try { _documentTabs.TabPages.Remove(tab.Page); }
            finally { _selectingTab = false; }
            tab.Page.Dispose();
            if (_documents.Count == 0) { _document = null; }
        }

        /// <summary>Saves every dirty tab (Save As dialog for untitled ones). Stops at the first cancel or failure.</summary>
        internal int SaveAll()
        {
            if (_runCts != null) return 0;
            var saved = 0;
            var origin = ActiveTab;
            foreach (var tab in Tabs.Where(t => t.Document.IsDirty).ToArray())
            {
                if (!ActivateTab(tab) || !SaveDocument(false)) { SetStatus("Save all stopped at " + tab.Document.Title + "; " + saved + " saved."); return saved; }
                saved++;
            }
            if (origin != null && FindTab(origin.Document.Id) != null) ActivateTab(origin);
            SetStatus(saved == 0 ? "Nothing to save." : "Saved " + saved + " DAX document(s).");
            return saved;
        }

        /// <summary>Checks every file-backed tab against disk. Returns how many tabs have an external change.</summary>
        internal int CheckExternalChanges()
        {
            if (_closing || IsDisposed) return 0;
            var count = 0;
            foreach (var tab in Tabs)
                if (CheckExternalChange(tab)) count++;
            return count;
        }

        private bool CheckExternalChange(WorkbenchTab tab)
        {
            var state = tab.Document.CheckDisk();
            var changed = state == DaxDiskState.Changed || state == DaxDiskState.Missing || state == DaxDiskState.Unreadable;
            var previous = tab.External;
            tab.External = changed ? state : DaxDiskState.Unchanged;
            if (previous != tab.External) UpdateTabTitle(tab);
            if (ReferenceEquals(tab, ActiveTab)) ShowExternalBanner(tab);
            return changed;
        }

        private void ShowExternalBanner(WorkbenchTab tab)
        {
            if (_externalBanner == null) return;
            var state = tab?.External ?? DaxDiskState.Unchanged;
            if (state != DaxDiskState.Changed && state != DaxDiskState.Missing && state != DaxDiskState.Unreadable)
            { _externalBanner.Visible = _externalBannerShown = false; return; }
            var name = tab.Document.Title;
            var dirty = tab.Document.IsDirty;
            _externalReload.Visible = state == DaxDiskState.Changed;
            _externalKeep.Text = state == DaxDiskState.Unreadable ? "Dismiss" : "Keep my version";
            _externalText.Text = state == DaxDiskState.Changed
                ? name + " changed on disk." + (dirty ? " Your unsaved edits are kept until you choose." : " This tab has no unsaved edits.") +
                  " Reload, or keep your version (the next Save overwrites the disk copy)."
                : state == DaxDiskState.Missing
                    ? name + " was deleted or moved on disk. Keep your version to recreate it on the next Save, or Save As elsewhere."
                    : name + " cannot be read right now (locked or access denied); it was not reloaded.";
            _externalBanner.Visible = _externalBannerShown = true;
        }

        /// <summary>Reloads the active tab from disk. Unsaved edits are discarded only after an explicit confirmation.</summary>
        internal bool ReloadActiveFromDisk()
        {
            var tab = ActiveTab;
            if (tab == null || _runCts != null || tab.Document.FilePath == null) return false;
            if (tab.Document.IsDirty && !WorkspaceDialogs.ConfirmReloadDiscardingEdits(this, tab.Document.Title))
            { SetStatus("Reload cancelled; your edits are kept."); return false; }
            var path = tab.Document.FilePath;
            if (!DocumentOperation(() => { _document.Open(path); DisplayDocument(); })) return false;
            SetStatus("Reloaded " + path + " from disk.");
            return true;
        }

        /// <summary>Keeps the buffer and accepts the current disk state as the save baseline (explicit overwrite on next Save).</summary>
        internal bool KeepActiveVersion()
        {
            var tab = ActiveTab;
            if (tab == null) return false;
            var state = tab.External == DaxDiskState.Unreadable ? DaxDiskState.Unreadable : tab.Document.AcknowledgeExternalChange();
            tab.External = DaxDiskState.Unchanged;
            UpdateTabTitle(tab);
            UpdateDocumentTitle();
            ShowExternalBanner(tab);
            SetStatus(state == DaxDiskState.Changed ? "Keeping your version; the next Save overwrites the changed file."
                : state == DaxDiskState.Missing ? "Keeping your version; the next Save recreates the file."
                : "Notice dismissed; the file was not reloaded.");
            ScheduleRecovery();
            return true;
        }
    }
}
