using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using PbiBench.Core.Navigation;

namespace TabularEditor.PbiBench.Navigation
{
    /// <summary>
    /// Lightweight code-only Quick Open dialog. The dialog only searches neutral descriptors;
    /// resolving/navigating the selected object remains the responsibility of UIController.
    /// </summary>
    internal sealed class PbiBenchQuickOpenForm : Form
    {
        private readonly IReadOnlyList<SemanticObjectDescriptor> _descriptors;
        private readonly TextBox _queryBox;
        private readonly ListView _results;
        private readonly Label _status;

        public PbiBenchQuickOpenForm(IReadOnlyList<SemanticObjectDescriptor> descriptors)
        {
            _descriptors = descriptors ?? throw new ArgumentNullException(nameof(descriptors));

            Text = "PbiBench Quick Open";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(680, 380);
            Size = new Size(860, 540);
            KeyPreview = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;

            var top = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 10, 10, 6) };
            _queryBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 11f),
                AccessibleName = "Quick Open search"
            };
            _queryBox.TextChanged += (s, e) => RefreshResults();
            top.Controls.Add(_queryBox);

            _results = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                ShowItemToolTips = true
            };
            _results.Columns.Add("Name", 230);
            _results.Columns.Add("Kind", 120);
            _results.Columns.Add("Location", 460);
            _results.DoubleClick += (s, e) => AcceptSelection();

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(10, 5, 10, 5) };
            _status = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            footer.Controls.Add(_status);

            Controls.Add(_results);
            Controls.Add(footer);
            Controls.Add(top);

            RefreshResults();
        }

        public string SelectedId { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _queryBox.Focus();
            _queryBox.SelectAll();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            if (keyData == Keys.Enter)
            {
                AcceptSelection();
                return true;
            }

            if (keyData == Keys.Down)
            {
                MoveSelection(1);
                return true;
            }

            if (keyData == Keys.Up)
            {
                MoveSelection(-1);
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void RefreshResults()
        {
            var matches = QuickOpenMatcher.Search(_descriptors, _queryBox.Text, 100);

            _results.BeginUpdate();
            try
            {
                _results.Items.Clear();
                foreach (var match in matches)
                {
                    var descriptor = match.Object;
                    var name = descriptor.Name;
                    if (descriptor.IsHidden) name += "  (hidden)";

                    var row = new ListViewItem(name)
                    {
                        Tag = descriptor.Id,
                        ToolTipText = descriptor.EffectivePath + "\r\nMatch: " + match.MatchKind + " · Score: " + match.Score
                    };
                    row.SubItems.Add(GetKindLabel(descriptor.Kind));
                    row.SubItems.Add(descriptor.EffectivePath);
                    _results.Items.Add(row);
                }

                if (_results.Items.Count > 0)
                {
                    _results.Items[0].Selected = true;
                    _results.Items[0].Focused = true;
                }
            }
            finally
            {
                _results.EndUpdate();
            }

            _status.Text = matches.Count + " result" + (matches.Count == 1 ? string.Empty : "s")
                + "  ·  Ctrl+P Quick Open  ·  filters: kind:measure, kind:table, kind:column, kind:fn, kind:ci";
        }

        private void MoveSelection(int delta)
        {
            if (_results.Items.Count == 0) return;

            var current = _results.SelectedIndices.Count > 0 ? _results.SelectedIndices[0] : 0;
            var next = Math.Max(0, Math.Min(_results.Items.Count - 1, current + delta));

            _results.SelectedItems.Clear();
            _results.Items[next].Selected = true;
            _results.Items[next].Focused = true;
            _results.EnsureVisible(next);
            _queryBox.Focus();
        }

        private void AcceptSelection()
        {
            if (_results.SelectedItems.Count != 1) return;

            SelectedId = _results.SelectedItems[0].Tag as string;
            if (string.IsNullOrWhiteSpace(SelectedId)) return;

            DialogResult = DialogResult.OK;
            Close();
        }

        private static string GetKindLabel(SemanticObjectKind kind)
        {
            switch (kind)
            {
                case SemanticObjectKind.CalculationGroup: return "Calc group";
                case SemanticObjectKind.CalculationItem: return "Calc item";
                case SemanticObjectKind.DataSource: return "Data source";
                default: return kind.ToString();
            }
        }
    }
}
