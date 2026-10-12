using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Dax
{
    internal sealed partial class PbiBenchDaxWorkbenchForm
    {
        internal Func<Model> AuthoringModel;
        internal Action<ITabularNamedObject> NavigateToObject;
        private IReadOnlyList<DaxDiagnostic> _advisoryIssues = Array.Empty<DaxDiagnostic>();
        private long _advisoryRevision = -1;
        private int _advisoryIndex;

        private void InitializeAuthoring()
        {
            var menu = new ToolStripMenuItem("&Authoring");
            menu.DropDownItems.Add(DocumentCommand("Complete at caret…", Keys.Control | Keys.Space, ShowAuthoring));
            menu.DropDownItems.Add(DocumentCommand("Parameter help", Keys.Control | Keys.Shift | Keys.Space, () => InspectStructure(true)));
            menu.DropDownItems.Add(DocumentCommand("Check DAX (advisory)", Keys.Control | Keys.F7, () => InspectStructure(false)));
            menu.DropDownItems.Add(DocumentCommand("Next advisory issue", Keys.F8, NextAdvisoryIssue));
            MainMenuStrip.Items.Add(menu);
        }

        private void InspectStructure(bool parameters)
        {
            if (_queryEditor.TextLength > DaxAuthoring.MaxTextLength) { SetStatus("Local assistance is limited to 256 Ki characters."); return; }
            var names = PbiBenchDaxModelBinder.Bind(AuthoringModel?.Invoke());
            if (parameters)
            {
                SetStatus(DaxCompletionContext.Analyze(_queryEditor.Text, _queryEditor.SelectionStart).SignatureHelp(names, DaxSignatureCatalog.Default));
                return;
            }
            _advisoryIssues = DaxDiagnostics.Analyze(_queryEditor.Text, names, DaxSignatureCatalog.Default);
            _advisoryRevision = _textRevision;
            _advisoryIndex = -1;
            if (_advisoryIssues.Count == 0) { SetStatus("No advisory DAX issues found · local lexical check, not engine validation."); return; }
            NextAdvisoryIssue();
        }

        private void NextAdvisoryIssue()
        {
            if (_advisoryRevision != _textRevision) { InspectStructure(false); return; }
            if (_advisoryIssues.Count == 0) { SetStatus("No advisory DAX issues found · local lexical check, not engine validation."); return; }
            _advisoryIndex = (_advisoryIndex + 1) % _advisoryIssues.Count;
            var issue = _advisoryIssues[_advisoryIndex];
            _queryEditor.Select(Math.Min(issue.Start, _queryEditor.TextLength), Math.Min(issue.Length, Math.Max(0, _queryEditor.TextLength - issue.Start)));
            _queryEditor.ScrollToCaret();
            _queryEditor.Focus();
            SetStatus((_advisoryIndex + 1) + "/" + _advisoryIssues.Count + " advisory · " + issue.Code + " " + issue.Message + " · F8 next.");
        }

        /// <summary>Captures caret context, fresh model names and document identity for one completion.</summary>
        internal DaxCompletionSession BeginWorkbenchCompletion()
        {
            if (_queryEditor.TextLength > DaxAuthoring.MaxTextLength) return null;
            var model = AuthoringModel?.Invoke();
            var names = PbiBenchDaxModelBinder.Bind(model);
            var context = DaxCompletionContext.Analyze(_queryEditor.Text, _queryEditor.SelectionStart);
            return new DaxCompletionSession {
                Model = model, Text = _queryEditor.Text, Caret = _queryEditor.SelectionStart, SelectionLength = _queryEditor.SelectionLength,
                Revision = names.Revision, Context = context, Items = context.Suggest(names, DaxSignatureCatalog.Default),
                Generation = _documentGeneration, TextRevision = _textRevision
            };
        }

        /// <summary>Inserts only when document, text, model identity and model names are unchanged.</summary>
        internal bool TryApplyWorkbenchCompletion(DaxCompletionSession session, DaxAuthoringItem item, out string reason)
        {
            reason = "";
            if (session == null || item == null) { reason = "Nothing selected."; return false; }
            if (session.Generation != _documentGeneration || session.TextRevision != _textRevision || !ReferenceEquals(session.Model, AuthoringModel?.Invoke()))
            { reason = "Insertion discarded because the document or model changed."; return false; }
            if (PbiBenchDaxModelBinder.Bind(session.Model).Revision != session.Revision)
            { reason = "Model objects were renamed, added or deleted; reopen completion."; return false; }
            if (session.SelectionLength > 0) _queryEditor.Select(session.Caret, session.SelectionLength);
            else _queryEditor.Select(session.Context.ReplaceStart, session.Context.ReplaceLength);
            _queryEditor.SelectedText = item.InsertText;
            return true;
        }

        private void ShowAuthoring()
        {
            if (_runCts != null || _closing) return;
            var session = BeginWorkbenchCompletion();
            if (session == null) { SetStatus("Local assistance is limited to 256 Ki characters."); return; }
            if (!session.Context.AllowsCompletion) { SetStatus("No completion inside strings, comments or new VAR names."); return; }
            var model = session.Model;
            Func<DaxAuthoringItem, ITabularNamedObject> resolve = i => PbiBenchDaxModelBinder.Resolve(model, i, session.Context.TableName);
            var selected = PbiBenchDaxCompletionPicker.Pick(this, session.Items, "Local DAX assistance", session.Context.Prefix,
                NavigateToObject == null ? null : (Action<DaxAuthoringItem>)(i => {
                    var obj = resolve(i);
                    if (obj != null && !obj.IsRemoved && ReferenceEquals(model, AuthoringModel?.Invoke())) NavigateToObject?.Invoke(obj);
                }),
                i => resolve(i) != null);
            if (selected == null) return;
            if (!TryApplyWorkbenchCompletion(session, selected, out var reason)) { SetStatus(reason); return; }
            _queryEditor.Focus();
        }
    }
}
