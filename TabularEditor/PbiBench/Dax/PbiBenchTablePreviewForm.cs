using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Dax
{
    internal sealed class PbiBenchTablePreviewForm : Form
    {
        internal string Query { get; private set; }
        internal int RowLimit { get; private set; }
        internal Func<bool> IsCurrent { get; private set; }
        internal PbiBenchTablePreviewForm(Model model)
        {
            Text = "TE2 Enhanced · Table preview query"; Size = new Size(720, 440); MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = new Padding(12), AutoScroll = true };
            var table = new ComboBox { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
            table.Items.AddRange(model.Tables.ToArray());
            var column = new ComboBox { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
            var limit = new NumericUpDown { Minimum = 1, Maximum = 5000, Value = 100, Width = 150, AccessibleName = "Maximum retained rows" };
            var filter = new CheckBox { Text = "Filter selected column", AutoSize = true };
            var kind = new ComboBox { Width = 160, DropDownStyle = ComboBoxStyle.DropDownList };
            kind.Items.AddRange(new object[] { "Text", "Decimal (invariant)", "Boolean", "Blank" }); kind.SelectedIndex = 0;
            var value = new TextBox { Width = 420, MaxLength = 4096 };
            var sort = new CheckBox { Text = "Sort by selected column", AutoSize = true };
            var descending = new CheckBox { Text = "Descending", AutoSize = true };
            var create = new Button { Text = "Open query draft", AutoSize = true };
            var status = new Label { Width = 650, Height = 66,
                Text = "Equality filter uses a typed literal; Blank uses ISBLANK.\nTOPN ties can exceed the requested rows; the Workbench caps retained rows.\nUnsorted previews have unspecified order. Limits do not bound server work." };
            panel.Controls.AddRange(new Control[] { table, column, limit, filter, kind, value, sort, descending, create, status });
            Controls.Add(panel);
            table.SelectedIndexChanged += (s, e) => { column.Items.Clear(); if (table.SelectedItem is Table selected)
                { column.Items.AddRange(selected.Columns.ToArray()); if (column.Items.Count > 0) column.SelectedIndex = 0; } };
            if (table.Items.Count > 0) table.SelectedIndex = 0;
            create.Click += (s, e) => {
                if (!(table.SelectedItem is Table selected) || selected.IsRemoved) { status.Text = "Select a current model table."; return; }
                var selectedColumn = column.SelectedItem as Column;
                if ((filter.Checked || sort.Checked) && (selectedColumn == null || selectedColumn.IsRemoved || !ReferenceEquals(selectedColumn.Table, selected)))
                { status.Text = "Select a current column for filtering/sorting."; return; }
                try {
                    object literal = null;
                    if (filter.Checked) {
                        if (kind.SelectedIndex == 0) literal = value.Text;
                        else if (kind.SelectedIndex == 1) literal = decimal.Parse(value.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                        else if (kind.SelectedIndex == 2) literal = bool.Parse(value.Text);
                    }
                    RowLimit = (int)limit.Value;
                    Query = DaxTablePreview.Build(selected.Name, RowLimit, selectedColumn?.Name, filter.Checked,
                        PreviewComparison.Equal, literal, sort.Checked, descending.Checked);
                    var tableName = selected.Name;
                    var columnName = selectedColumn?.Name;
                    var needsColumn = filter.Checked || sort.Checked;
                    IsCurrent = () => !selected.IsRemoved && selected.Name == tableName && (!needsColumn ||
                        (selectedColumn != null && !selectedColumn.IsRemoved && selectedColumn.Name == columnName && ReferenceEquals(selectedColumn.Table, selected)));
                    DialogResult = DialogResult.OK; Close();
                }
                catch (Exception) { status.Text = "Check the typed value: decimal uses a dot, Boolean uses true/false. No query was executed."; }
            };
        }
    }
}
