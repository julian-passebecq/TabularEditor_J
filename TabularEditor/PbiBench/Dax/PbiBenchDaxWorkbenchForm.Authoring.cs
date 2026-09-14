using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Navigation;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Dax
{
    internal sealed partial class PbiBenchDaxWorkbenchForm
    {
        internal Func<Model> AuthoringModel;
        internal Action<ITabularNamedObject> NavigateToObject;

        private void InitializeAuthoring()
        {
            var menu = new ToolStripMenuItem("&Authoring");
            menu.DropDownItems.Add(DocumentCommand("Insert function / model object…", Keys.Control | Keys.Space, ShowAuthoring));
            menu.DropDownItems.Add(DocumentCommand("Parameter help", Keys.Control | Keys.Shift | Keys.Space, () => InspectStructure(true)));
            menu.DropDownItems.Add(DocumentCommand("Check delimiters (advisory)", Keys.Control | Keys.F7, () => InspectStructure(false)));
            MainMenuStrip.Items.Add(menu);
        }

        private void InspectStructure(bool parameters)
        {
            if (_queryEditor.TextLength > DaxAuthoring.MaxTextLength) { SetStatus("Local assistance is limited to 256 Ki characters."); return; }
            var tokens = PbiBenchDaxAuthoring.Tokens(_queryEditor.Text);
            if (parameters) { SetStatus(DaxAuthoring.ParameterHelp(tokens, _queryEditor.SelectionStart)); return; }
            var issues = DaxAuthoring.CheckStructure(tokens);
            if (issues.Count == 0) { SetStatus("No delimiter issues found · advisory only; not engine validation."); return; }
            var issue = issues[0];
            _queryEditor.Select(issue.Start, issue.Length);
            _queryEditor.ScrollToCaret();
            _queryEditor.Focus();
            SetStatus(issues.Count + " delimiter issue(s) · " + issue.Message + " · advisory only.");
        }

        private void ShowAuthoring()
        {
            if (_runCts != null || _closing) return;
            var model = AuthoringModel?.Invoke();
            var index = model == null ? null : PbiBenchQuickOpenIndex.Build(model);
            var items = PbiBenchDaxAuthoring.Items(index);
            var generation = _documentGeneration;
            var revision = _textRevision;
            using (var dialog = new Form { Text = "Local DAX assistance", StartPosition = FormStartPosition.CenterParent,
                Size = new Size(760, 480), MinimumSize = new Size(540, 340), ShowInTaskbar = false })
            {
                var filter = new TextBox { Dock = DockStyle.Top };
                var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "InsertText" };
                var detail = new Label { Dock = DockStyle.Bottom, Height = 50, Text = "Insert at caret / replace selection. Filter by name. Maximum 200 matches. No network." };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
                var insert = new Button { Text = "Insert", DialogResult = DialogResult.OK };
                var navigate = new Button { Text = "Go to object", AutoSize = true };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
                buttons.Controls.AddRange(new Control[] { insert, navigate, cancel });
                dialog.Controls.Add(list); dialog.Controls.Add(filter); dialog.Controls.Add(detail); dialog.Controls.Add(buttons);
                dialog.AcceptButton = insert; dialog.CancelButton = cancel;
                Action refresh = () => { list.DataSource = DaxAuthoring.Search(items, filter.Text).ToList(); };
                list.SelectedIndexChanged += (s, e) => {
                    var item = list.SelectedItem as DaxAuthoringItem;
                    detail.Text = item == null ? "No matches." : item.Kind + " · " + item.Detail;
                    insert.Enabled = item != null;
                    navigate.Enabled = item != null && item.ObjectId.Length > 0 && NavigateToObject != null;
                };
                navigate.Click += (s, e) => {
                    var item = list.SelectedItem as DaxAuthoringItem;
                    var obj = item == null ? null : index?.Resolve(item.ObjectId);
                    if (obj != null && !obj.IsRemoved && ReferenceEquals(model, AuthoringModel?.Invoke())) NavigateToObject?.Invoke(obj);
                };
                filter.TextChanged += (s, e) => refresh();
                refresh();
                if (dialog.ShowDialog(this) != DialogResult.OK || !(list.SelectedItem is DaxAuthoringItem selected)) return;
                if (generation != _documentGeneration || revision != _textRevision || !ReferenceEquals(model, AuthoringModel?.Invoke()))
                { SetStatus("Insertion discarded because the document or model changed."); return; }
                if (selected.ObjectId.Length > 0 && (index.Resolve(selected.ObjectId)?.IsRemoved != false ||
                    (index.Resolve(selected.ObjectId) as IDaxObject)?.DaxObjectFullName != selected.InsertText))
                { SetStatus("Object changed; reopen local assistance."); return; }
                _queryEditor.SelectedText = selected.InsertText;
                _queryEditor.Focus();
            }
        }
    }
}
