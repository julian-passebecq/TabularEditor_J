using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Optimize
{
    /// <summary>
    /// OPT-001 results: semantic assertions and curated BPA findings in one read-only list. Enter or
    /// double-click navigates through the supplied TE2 Goto delegate; nothing is fixed or written.
    /// </summary>
    internal sealed class PbiBenchAssertionsForm : Form
    {
        private readonly Func<string, bool, PbiBenchCheckResult> _run;
        private readonly Action<ITabularNamedObject> _navigate;
        private readonly ListView _list;
        private readonly TextBox _allowlist;
        private readonly CheckBox _includeUserRules;
        private readonly Label _summary;
        internal PbiBenchCheckResult Result { get; private set; }
        internal int RowCount => _list.Items.Count;

        /// <param name="run">Runs the checks for (allowlist text, include loaded BPA rules).</param>
        internal PbiBenchAssertionsForm(Func<string, bool, PbiBenchCheckResult> run, Action<ITabularNamedObject> navigate, string allowlist)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _navigate = navigate;
            Text = "TE2 Enhanced · Model checks (curated BPA + assertions)";
            Size = new Size(1000, 600); MinimumSize = new Size(720, 420);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true;

            var top = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(8, 8, 8, 0) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var allowLabel = new Label { Text = "Bi-directional allowlist\n(relationship name or From -> To, one per line):", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top };
            _allowlist = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 54, Dock = DockStyle.Fill, Text = allowlist ?? "",
                AccessibleName = "Bi-directional relationship allowlist", MaxLength = 64 * 1024 };
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            var runButton = new Button { Text = "&Run checks", AutoSize = true };
            _includeUserRules = new CheckBox { Text = "Also run my loaded BPA rules", AutoSize = true,
                AccessibleDescription = "Same-ID user, external or model rules take precedence over curated rules." };
            buttons.Controls.AddRange(new Control[] { runButton, _includeUserRules });
            top.Controls.Add(allowLabel, 0, 0); top.Controls.Add(_allowlist, 1, 0); top.Controls.Add(buttons, 2, 0);

            _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false,
                AccessibleName = "Model check findings" };
            _list.Columns.Add("Check", 150); _list.Columns.Add("Severity", 60); _list.Columns.Add("Object", 260);
            _list.Columns.Add("Title", 260); _list.Columns.Add("Detail", 400);
            _list.Groups.Add("Assertion", "Semantic assertions (read-only)");
            _list.Groups.Add("BPA", "Curated BPA pack (TE2 rule engine; no rule file written)");

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            var go = new Button { Text = "&Go to object", AutoSize = true };
            _summary = new Label { AutoSize = true, Padding = new Padding(0, 6, 12, 0), Text = "Advisory: checks read the model only; fix in TE2 with its normal undo." };
            bottom.Controls.AddRange(new Control[] { close, go, _summary });
            CancelButton = close;

            Controls.Add(_list); Controls.Add(top); Controls.Add(bottom);
            runButton.Click += (s, e) => RunChecks();
            go.Click += (s, e) => NavigateSelected();
            _list.DoubleClick += (s, e) => NavigateSelected();
            _list.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; NavigateSelected(); } };
            Shown += (s, e) => RunChecks();
        }

        internal void RunChecks()
        {
            Result = _run(_allowlist.Text, _includeUserRules.Checked);
            _list.BeginUpdate();
            try
            {
                _list.Items.Clear();
                foreach (var row in Result.Rows)
                {
                    var item = new ListViewItem(new[] { row.Code, row.Severity.ToString(), row.ObjectPath, row.Title, row.Message }) {
                        Tag = row, Group = _list.Groups[row.Group]
                    };
                    if (row.Target == null) item.ForeColor = SystemColors.GrayText;
                    _list.Items.Add(item);
                }
            }
            finally { _list.EndUpdate(); }
            var failing = Result.Assertions.Outcomes.Where(o => !o.Passed).Select(o => o.Definition.Id + " " + o.Failed);
            _summary.Text = Result.Summary + (failing.Any() ? " · " + string.Join(", ", failing) : "");
            if (_list.Items.Count > 0) { _list.Items[0].Selected = true; _list.Items[0].Focused = true; }
        }

        /// <summary>Navigates row <paramref name="index"/>; returns the object handed to Goto, or null (removed/not navigable).</summary>
        internal ITabularNamedObject NavigateRow(int index)
        {
            if (index < 0 || index >= _list.Items.Count) return null;
            var row = (PbiBenchCheckRow)_list.Items[index].Tag;
            var target = row.Target;
            if (target == null || target.IsRemoved) { _summary.Text = "This row has no navigable object (removed, rule error or model-level)."; return null; }
            _navigate?.Invoke(target);
            return target;
        }

        private void NavigateSelected()
        {
            if (_list.SelectedIndices.Count == 1) NavigateRow(_list.SelectedIndices[0]);
        }
    }
}
