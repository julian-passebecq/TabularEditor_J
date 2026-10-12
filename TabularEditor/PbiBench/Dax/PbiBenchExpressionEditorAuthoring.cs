using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using FastColoredTextBoxNS;
using PbiBench.Core.Dax;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Dax
{
    /// <summary>Minimal editor seam so authoring logic is testable without TE2's main form.</summary>
    internal interface IDaxEditorSurface
    {
        string Text { get; }
        int Caret { get; }
        /// <summary>Replaces a range as one editor undo step and leaves the caret after the insert.</summary>
        void Replace(int start, int length, string text);
        void Undo();
        void Reset(string text, int caret);
        void Select(int start, int length);
    }

    /// <summary>Adapter over TE2's FastColoredTextBox expression editor (no editor replacement).</summary>
    internal sealed class PbiBenchFctbSurface : IDaxEditorSurface
    {
        private readonly FastColoredTextBox _editor;
        internal PbiBenchFctbSurface(FastColoredTextBox editor) { _editor = editor ?? throw new ArgumentNullException(nameof(editor)); }

        /// <summary>A detached TE2 expression-editor control, for smoke checks.</summary>
        internal static IDaxEditorSurface CreateDetached() => new PbiBenchFctbSurface(new TabularEditor.UI.Extensions.FCTB.FastColoredTextBox());

        public string Text => _editor.Text;
        public int Caret => _editor.SelectionStart;

        public void Replace(int start, int length, string text)
        {
            _editor.BeginAutoUndo();
            try
            {
                _editor.Selection = new Range(_editor, _editor.PositionToPlace(start), _editor.PositionToPlace(start + length));
                _editor.InsertText(text);
            }
            finally { _editor.EndAutoUndo(); }
        }

        public void Undo() => _editor.Undo();
        public void Reset(string text, int caret) { _editor.Text = text; _editor.SelectionStart = caret; _editor.SelectionLength = 0; }
        public void Select(int start, int length) { _editor.SelectionStart = start; _editor.SelectionLength = length; }
    }

    /// <summary>A completion offer captured at one text/model state; applying it re-checks that state.</summary>
    internal sealed class DaxCompletionSession
    {
        public Model Model { get; set; }
        public string Text { get; set; } = "";
        public int Caret { get; set; }
        public string Revision { get; set; } = "";
        public DaxCompletionContext Context { get; set; }
        public IReadOnlyList<DaxAuthoringItem> Items { get; set; } = Array.Empty<DaxAuthoringItem>();
        // Workbench-only identity (document generation + text revision + selection).
        public long Generation { get; set; }
        public long TextRevision { get; set; }
        public int SelectionLength { get; set; }
    }

    /// <summary>
    /// Opt-in local DAX assistance for TE2's expression editor: context-aware completion
    /// (Ctrl+Space), signature help (Ctrl+Shift+Space) and advisory checks, attached through
    /// KeyDown events only. Insertions go through the editor's own undo stack; TE2 still owns
    /// committing the expression to the model (its normal accept/undo batch).
    /// </summary>
    internal sealed class PbiBenchExpressionEditorAuthoring
    {
        private readonly IDaxEditorSurface _surface;
        private readonly Func<Model> _model;

        internal PbiBenchExpressionEditorAuthoring(IDaxEditorSurface surface, Func<Model> model)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            _model = model ?? throw new ArgumentNullException(nameof(model));
        }

        internal IDaxEditorSurface Surface => _surface;

        internal DaxCompletionSession BeginCompletion()
        {
            var text = _surface.Text ?? "";
            if (text.Length > DaxAuthoring.MaxTextLength) return null;
            var model = _model();
            var names = PbiBenchDaxModelBinder.Bind(model);
            var context = DaxCompletionContext.Analyze(text, _surface.Caret);
            return new DaxCompletionSession {
                Model = model, Text = text, Caret = _surface.Caret, Revision = names.Revision, Context = context,
                Items = context.Suggest(names, DaxSignatureCatalog.Default)
            };
        }

        /// <summary>Applies a session item only if text, caret, model identity and model names are unchanged.</summary>
        internal bool TryApply(DaxCompletionSession session, DaxAuthoringItem item, out string reason)
        {
            reason = "";
            if (session == null || item == null) { reason = "Nothing selected."; return false; }
            if (!ReferenceEquals(session.Model, _model())) { reason = "Insertion discarded: a different model is loaded."; return false; }
            if (!string.Equals(session.Text, _surface.Text, StringComparison.Ordinal) || session.Caret != _surface.Caret)
            { reason = "Insertion discarded: the expression changed since the list was built."; return false; }
            if (PbiBenchDaxModelBinder.Bind(session.Model).Revision != session.Revision)
            { reason = "Insertion discarded: model objects were renamed, added or deleted. Reopen completion."; return false; }
            _surface.Replace(session.Context.ReplaceStart, session.Context.ReplaceLength, item.InsertText);
            return true;
        }

        internal string SignatureHelp()
        {
            var text = _surface.Text ?? "";
            if (text.Length > DaxAuthoring.MaxTextLength) return "Local assistance is limited to 256 Ki characters.";
            var model = _model();
            return DaxCompletionContext.Analyze(text, _surface.Caret).SignatureHelp(PbiBenchDaxModelBinder.Bind(model), DaxSignatureCatalog.Default);
        }

        internal IReadOnlyList<DaxDiagnostic> Check()
        {
            var text = _surface.Text ?? "";
            if (text.Length > DaxAuthoring.MaxTextLength) return Array.Empty<DaxDiagnostic>();
            return DaxDiagnostics.Analyze(text, PbiBenchDaxModelBinder.Bind(_model()), DaxSignatureCatalog.Default);
        }

        // ---- Opt-in attachment to TE2's expression editor -------------------------------------

        private static FastColoredTextBox _attachedEditor;
        private static PbiBenchExpressionEditorAuthoring _attached;
        private static Func<bool> _isDax;
        private static Action<string> _status;

        internal static bool IsEnabled => _attached != null;
        internal static PbiBenchExpressionEditorAuthoring Attached => _attached;

        internal static void Enable(FastColoredTextBox editor, Func<Model> model, Func<bool> isDax, Action<string> status)
        {
            if (editor == null || _attached != null) return;
            _attachedEditor = editor;
            _attached = new PbiBenchExpressionEditorAuthoring(new PbiBenchFctbSurface(editor), model);
            _isDax = isDax ?? (() => true);
            _status = status ?? (s => { });
            editor.KeyDown += OnKeyDown;
        }

        internal static void Disable()
        {
            if (_attachedEditor != null) _attachedEditor.KeyDown -= OnKeyDown;
            _attachedEditor = null; _attached = null; _isDax = null; _status = null;
        }

        private static void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_attached == null || e.KeyCode != Keys.Space || (e.Modifiers & Keys.Control) != Keys.Control || (e.Modifiers & Keys.Alt) == Keys.Alt) return;
            e.Handled = true;
            e.SuppressKeyPress = true;
            if (!_isDax()) { _status("Local DAX assistance: the current expression is not DAX."); return; }
            if ((e.Modifiers & Keys.Shift) == Keys.Shift) { _status(_attached.SignatureHelp()); return; }
            ShowCompletion(_attachedEditor.FindForm());
        }

        internal static void ShowCompletion(IWin32Window owner)
        {
            var authoring = _attached;
            if (authoring == null) return;
            var session = authoring.BeginCompletion();
            if (session == null) { _status?.Invoke("Local assistance is limited to 256 Ki characters."); return; }
            if (!session.Context.AllowsCompletion) { _status?.Invoke("No completion inside strings, comments or new VAR names."); return; }
            var item = PbiBenchDaxCompletionPicker.Pick(owner, session.Items, "Local DAX assistance · expression editor", session.Context.Prefix, null);
            if (item == null) return;
            _status?.Invoke(authoring.TryApply(session, item, out var reason) ? "Inserted " + item.InsertText + " · Ctrl+Z undoes · accept the edit as usual." : reason);
        }

        internal static void CheckCurrent(Action<int, int> select)
        {
            var authoring = _attached;
            if (authoring == null || _status == null) return;
            if (!_isDax()) { _status("Local DAX assistance: the current expression is not DAX."); return; }
            var issues = authoring.Check();
            if (issues.Count == 0) { _status("No advisory DAX issues found · local lexical check, not engine validation."); return; }
            select?.Invoke(issues[0].Start, issues[0].Length);
            _status(issues.Count + " advisory issue(s) · " + issues[0].Code + " " + issues[0].Message);
        }
    }
}
