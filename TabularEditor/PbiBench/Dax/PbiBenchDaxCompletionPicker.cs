using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Dax;

namespace TabularEditor.PbiBench.Dax
{
    /// <summary>Modal list shared by the DAX Workbench and the expression editor. No network.</summary>
    internal static class PbiBenchDaxCompletionPicker
    {
        internal static DaxAuthoringItem Pick(IWin32Window owner, IReadOnlyList<DaxAuthoringItem> items, string title, string initialFilter,
            Action<DaxAuthoringItem> navigate, Func<DaxAuthoringItem, bool> canNavigate = null)
        {
            using (var dialog = new Form { Text = title, StartPosition = FormStartPosition.CenterParent,
                Size = new Size(760, 480), MinimumSize = new Size(540, 340), ShowInTaskbar = false, AutoScaleMode = AutoScaleMode.Dpi })
            {
                var filter = new TextBox { Dock = DockStyle.Top, Text = initialFilter ?? "", AccessibleName = "Filter suggestions" };
                var list = new ListBox { Dock = DockStyle.Fill, DisplayMember = "InsertText", AccessibleName = "Suggestions" };
                var detail = new Label { Dock = DockStyle.Bottom, Height = 50, Text = "Replaces the word at the caret. Filter by name. Maximum 200 matches. No network." };
                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36 };
                var insert = new Button { Text = "Insert", DialogResult = DialogResult.OK };
                var go = new Button { Text = "Go to object", AutoSize = true, Visible = navigate != null };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
                buttons.Controls.AddRange(new Control[] { insert, go, cancel });
                dialog.Controls.Add(list); dialog.Controls.Add(filter); dialog.Controls.Add(detail); dialog.Controls.Add(buttons);
                dialog.AcceptButton = insert; dialog.CancelButton = cancel;
                Action refresh = () => {
                    list.DataSource = DaxAuthoring.Search(items, filter.Text).ToList();
                    if (list.Items.Count == 0) { detail.Text = "No matches."; insert.Enabled = false; go.Enabled = false; }
                };
                list.SelectedIndexChanged += (s, e) => {
                    var item = list.SelectedItem as DaxAuthoringItem;
                    detail.Text = item == null ? "No matches." : item.Kind + " · " + item.Detail;
                    insert.Enabled = item != null;
                    go.Enabled = item != null && navigate != null && (canNavigate?.Invoke(item) ?? false);
                };
                go.Click += (s, e) => { if (list.SelectedItem is DaxAuthoringItem item) navigate?.Invoke(item); };
                filter.KeyDown += (s, e) => {
                    if (list.Items.Count == 0) return;
                    if (e.KeyCode == Keys.Down) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); e.Handled = true; }
                    else if (e.KeyCode == Keys.Up) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); e.Handled = true; }
                };
                list.DoubleClick += (s, e) => { if (list.SelectedItem != null) { dialog.DialogResult = DialogResult.OK; dialog.Close(); } };
                filter.TextChanged += (s, e) => refresh();
                refresh();
                dialog.Shown += (s, e) => { filter.Focus(); filter.SelectionStart = filter.TextLength; };
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                return list.SelectedItem as DaxAuthoringItem;
            }
        }
    }
}
