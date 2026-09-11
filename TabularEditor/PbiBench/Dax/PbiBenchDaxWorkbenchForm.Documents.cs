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
        private readonly DaxDocument _document = new DaxDocument();
        private long _documentGeneration;
        private IDaxDocumentDialogs _documentDialogs;
        private ToolStripMenuItem _newDocument;
        private ToolStripMenuItem _openDocument;
        private ToolStripMenuItem _saveDocument;
        private ToolStripMenuItem _saveDocumentAs;

        private void InitializeDocuments(IDaxDocumentDialogs dialogs)
        {
            _documentDialogs = dialogs ?? new DaxDocumentDialogs();
            _document.New(_queryEditor.Text);
            _queryEditor.TextChanged += (s, e) =>
            {
                _document.Text = _queryEditor.Text;
                UpdateDocumentTitle();
            };
            var menu = new MenuStrip();
            var file = new ToolStripMenuItem("&File");
            _newDocument = DocumentCommand("&New", Keys.Control | Keys.N, NewDocument);
            _openDocument = DocumentCommand("&Open…", Keys.Control | Keys.O, OpenDocument);
            _saveDocument = DocumentCommand("&Save", Keys.Control | Keys.S, () => SaveDocument(false));
            _saveDocumentAs = DocumentCommand("Save &As…", Keys.Control | Keys.Shift | Keys.S, () => SaveDocument(true));
            file.DropDownItems.AddRange(new ToolStripItem[] { _newDocument, _openDocument,
                new ToolStripSeparator(), _saveDocument, _saveDocumentAs });
            menu.Items.Add(file);
            MainMenuStrip = menu;
            Controls.Add(menu);
            UpdateDocumentTitle();
        }

        private static ToolStripMenuItem DocumentCommand(string text, Keys shortcut, Action action)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeys = shortcut };
            item.Click += (s, e) => action();
            return item;
        }

        private void NewDocument()
        {
            if (_runCts != null || !ConfirmDocumentReplacement()) return;
            _document.New();
            DisplayDocument();
        }

        private void OpenDocument()
        {
            if (_runCts != null) return;
            var path = _documentDialogs.OpenPath(this);
            if (path == null || !ConfirmDocumentReplacement()) return;
            DocumentOperation(() => { _document.Open(path); DisplayDocument(); });
        }

        private bool SaveDocument(bool saveAs)
        {
            if (_runCts != null) return false;
            var path = !saveAs && _document.FilePath != null ? _document.FilePath
                : _documentDialogs.SavePath(this, _document.FilePath);
            if (path == null) return false;
            return DocumentOperation(() =>
            {
                _document.Save(path);
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
            // Called only after a successful New/Open/history replacement, even for equal text.
            _documentGeneration++;
            _queryEditor.Text = _document.Text;
            _queryEditor.ClearUndo();
            _currentResult = new DaxQueryResult();
            BindResult(_currentResult);
            UpdateDocumentTitle();
            ApplyExecutionAvailability();
            _queryEditor.Focus();
        }

        private void UpdateDocumentTitle() => Text = (_document.FilePath == null ? "Untitled.dax" : Path.GetFileName(_document.FilePath))
            + (_document.IsDirty ? " *" : "") + " — PbiBench DAX Workbench";

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
