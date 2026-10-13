using System;
using System.IO;
using System.Security;
using System.Windows.Forms;
using PbiBench.Core.Dax;

namespace TabularEditor.PbiBench.Dax
{
    public interface IDaxDocumentDialogs
    {
        string OpenPath(IWin32Window owner);
        string SavePath(IWin32Window owner, string currentPath);
        DialogResult SaveChanges(IWin32Window owner, string name);
        void Error(IWin32Window owner, string message);
    }

    internal sealed partial class PbiBenchDaxWorkbenchForm
    {
        // Active tab's document (swapped by .Tabs.cs). The commands below act on the active tab only.
        private DaxDocument _document;
        // Global, monotonic: every content replacement in any tab takes the next value (tab stamps stay unique).
        private long _documentGeneration;
        private IDaxDocumentDialogs _documentDialogs;
        private ToolStripMenuItem _newDocument;
        private ToolStripMenuItem _openDocument;
        private ToolStripMenuItem _saveDocument;
        private ToolStripMenuItem _saveDocumentAs;
        private ToolStripMenuItem _fileMenu;

        private void InitializeDocuments(IDaxDocumentDialogs dialogs)
        {
            _documentDialogs = dialogs ?? new DaxDocumentDialogs();
            var menu = new MenuStrip();
            _fileMenu = new ToolStripMenuItem("&File");
            _newDocument = DocumentCommand("&New tab", Keys.Control | Keys.N, () => NewTab());
            _openDocument = DocumentCommand("&Open…", Keys.Control | Keys.O, () => OpenInNewTab());
            _saveDocument = DocumentCommand("&Save", Keys.Control | Keys.S, () => SaveDocument(false));
            _saveDocumentAs = DocumentCommand("Save &As…", Keys.Control | Keys.Shift | Keys.S, () => SaveDocument(true));
            _fileMenu.DropDownItems.AddRange(new ToolStripItem[] { _newDocument, _openDocument, CreateOpenInCurrentTabCommand(),
                new ToolStripSeparator(), _saveDocument, _saveDocumentAs, CreateSaveAllCommand(),
                new ToolStripSeparator() });
            _fileMenu.DropDownItems.AddRange(CreateTabCommands());
            menu.Items.Add(_fileMenu);
            MainMenuStrip = menu;
            Controls.Add(menu);
            UpdateDocumentTitle();
        }

        internal void LoadPreviewDraft(string query, int rowLimit)
        {
            // A generated draft is starter text: untouched, it is not unsaved work and closing must not prompt (Light QA D17).
            _document.New(query);
            _maxRows.Value = rowLimit;
            DisplayDocument();
            SetStatus("Table preview draft · F5 executes. TOPN ties may exceed rows; retention limits do not bound server work.");
        }

        private static ToolStripMenuItem DocumentCommand(string text, Keys shortcut, Action action)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
            item.Click += (s, e) => action();
            return item;
        }

        /// <summary>Replaces the active tab with an empty draft (after the dirty prompt).</summary>
        private void NewDocument()
        {
            if (_runCts != null || !ConfirmDocumentReplacement()) return;
            _document.New();
            DisplayDocument();
        }

        /// <summary>Opens a file into the active tab (after the dirty prompt). A file open in another tab is activated instead.</summary>
        private void OpenDocument()
        {
            if (_runCts != null) return;
            var path = _documentDialogs.OpenPath(this);
            if (path == null) return;
            if (ActivateIfOpenElsewhere(path)) return;
            if (!ConfirmDocumentReplacement()) return;
            DocumentOperation(() => { _document.Open(path); DisplayDocument(); });
        }

        private bool SaveDocument(bool saveAs)
        {
            if (_runCts != null) return false;
            var path = !saveAs && _document.FilePath != null ? _document.FilePath
                : _documentDialogs.SavePath(this, _document.FilePath);
            if (path == null) return false;
            if (IsOpenInOtherTab(path))
            {
                _documentDialogs.Error(this, "This file is open in another Workbench tab. Close that tab or choose another name.");
                return false;
            }
            return DocumentOperation(() =>
            {
                _document.Save(path);
                OnActiveDocumentSaved();
                UpdateDocumentTitle();
                SetStatus("Saved " + _document.FilePath);
            });
        }

        private bool ConfirmDocumentReplacement()
        {
            if (!_document.IsDirty) return true;
            var answer = _documentDialogs.SaveChanges(this, _document.FilePath == null
                ? "Untitled.dax" : Path.GetFileName(_document.FilePath));
            return answer == DialogResult.No || (answer == DialogResult.Yes && SaveDocument(false));
        }

        private bool DocumentOperation(Action action)
        {
            try { action(); return true; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException ||
                ex is ArgumentException || ex is NotSupportedException || ex is SecurityException)
            {
                _documentDialogs.Error(this, ex.Message);
                return false;
            }
        }

        private void DisplayDocument()
        {
            // Called only after a successful New/Open/history/reload replacement, even for equal text.
            var tab = ActiveTab;
            _documentGeneration++;
            tab.Generation = _documentGeneration;
            tab.External = DaxDiskState.Unchanged;
            tab.TitleSuffix = "";
            _queryEditor.Text = _document.Text;
            _queryEditor.ClearUndo();
            _currentResult = new DaxQueryResult();
            BindExecutionResult(_currentResult);
            OnActiveContentReplaced();
            UpdateTabTitle(tab);
            UpdateDocumentTitle();
            ShowExternalBanner(tab);
            ApplyExecutionAvailability();
            _queryEditor.Focus();
        }

        private void UpdateDocumentTitle() => Text = (_document == null ? "Untitled.dax" : _document.Title)
            + (_document != null && _document.IsDirty ? " *" : "") + " — PbiBench DAX Workbench"
            + (_documents.Count > 1 ? " (" + (_documents.ActiveIndex + 1) + "/" + _documents.Count + ")" : "");

        private sealed class DaxDocumentDialogs : IDaxDocumentDialogs
        {
            public string OpenPath(IWin32Window owner)
            {
                using (var dialog = new OpenFileDialog { Filter = "DAX files (*.dax)|*.dax|Text files (*.txt)|*.txt|All files (*.*)|*.*" })
                    return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
            }
            public string SavePath(IWin32Window owner, string currentPath)
            {
                using (var dialog = new SaveFileDialog { Filter = "DAX files (*.dax)|*.dax", DefaultExt = "dax",
                    AddExtension = true, OverwritePrompt = true, FileName = currentPath ?? "Untitled.dax" })
                    return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
            }
            public DialogResult SaveChanges(IWin32Window owner, string name) => MessageBox.Show(owner,
                "Save changes to " + name + "?", "Unsaved DAX", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            public void Error(IWin32Window owner, string message) => MessageBox.Show(owner, message,
                "DAX document", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
