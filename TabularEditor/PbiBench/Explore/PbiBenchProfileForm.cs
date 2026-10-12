using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Explore;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Explore
{
    /// <summary>
    /// EXP-002 draft builder: column profile or relationship coverage. The result is query text only;
    /// the caller opens it as an unsaved Workbench draft and nothing runs until the user presses F5.
    /// </summary>
    internal sealed class PbiBenchProfileForm : Form
    {
        internal string Query { get; private set; }
        internal int RowLimit { get; private set; }
        internal Func<bool> IsCurrent { get; private set; } = () => false;
        internal readonly RadioButton ColumnMode;
        internal readonly RadioButton RelationshipMode;
        internal readonly ComboBox Tables;
        internal readonly ComboBox Columns;
        internal readonly ComboBox Relationships;
        internal readonly NumericUpDown Limit;
        private readonly Label _status;

        internal PbiBenchProfileForm(Model model)
        {
            Text = "TE2 Enhanced · Profile query draft"; Size = new Size(720, 460); MinimumSize = Size;
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                Padding = new Padding(12), AutoScroll = true };
            ColumnMode = new RadioButton { Text = "&Column profile (rows, distinct, blank, min/max, top values)", AutoSize = true, Checked = true };
            Tables = new ComboBox { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", AccessibleName = "Table" };
            Tables.Items.AddRange(model.Tables.ToArray());
            Columns = new ComboBox { Width = 420, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name", AccessibleName = "Column" };
            RelationshipMode = new RadioButton { Text = "&Relationship coverage (unmatched and unused keys)", AutoSize = true };
            Relationships = new ComboBox { Width = 640, DropDownStyle = ComboBoxStyle.DropDownList, AccessibleName = "Relationship" };
            foreach (var r in model.Relationships.OfType<SingleColumnRelationship>().Where(r => r.FromColumn != null && r.ToColumn != null))
                Relationships.Items.Add(new RelationshipChoice(r));
            if (Relationships.Items.Count > 0) Relationships.SelectedIndex = 0;
            var limitLabel = new Label { AutoSize = true, Text = "Top values (column, 1-100) or unmatched key samples (relationship, 1-1000):" };
            Limit = new NumericUpDown { Minimum = 1, Maximum = DaxProfileQueryBuilder.MaxSampleRows, Value = DaxProfileQueryBuilder.DefaultTopValues,
                Width = 150, AccessibleName = "Row limit" };
            var create = new Button { Text = "Open query draft", AutoSize = true };
            _status = new Label { Width = 650, Height = 70, Text =
                "Draft only: it opens unsaved in the DAX Workbench and runs only when you press F5.\n" +
                "Row limits bound returned rows; COUNTROWS/DISTINCTCOUNT/EXCEPT still scan whole columns on the server." };
            panel.Controls.AddRange(new Control[] { ColumnMode, Tables, Columns, RelationshipMode, Relationships, limitLabel, Limit, create, _status });
            Controls.Add(panel);
            AcceptButton = create;
            Tables.SelectedIndexChanged += (s, e) => {
                Columns.Items.Clear();
                if (Tables.SelectedItem is Table t) { Columns.Items.AddRange(t.Columns.ToArray()); if (Columns.Items.Count > 0) Columns.SelectedIndex = 0; }
            };
            if (Tables.Items.Count > 0) Tables.SelectedIndex = 0;
            create.Click += (s, e) => { if (TryCreate(out var error)) { DialogResult = DialogResult.OK; Close(); } else _status.Text = error; };
        }

        /// <summary>Builds the draft from the current selection; false with a reason when the input is not usable.</summary>
        internal bool TryCreate(out string error)
        {
            error = "";
            try
            {
                var limit = (int)Limit.Value;
                if (ColumnMode.Checked)
                {
                    if (!(Columns.SelectedItem is Column column) || column.IsRemoved || column.Table == null || column.Table.IsRemoved)
                    { error = "Select a current model column."; return false; }
                    if (limit > DaxProfileQueryBuilder.MaxTopValues) { error = "Top values must be between 1 and " + DaxProfileQueryBuilder.MaxTopValues + "."; return false; }
                    var draft = CreateColumnDraft(column, limit);
                    var table = column.Table; var tableName = table.Name; var columnName = column.Name;
                    Query = draft.Query; RowLimit = draft.RowLimit;
                    IsCurrent = () => !column.IsRemoved && !table.IsRemoved && ReferenceEquals(column.Table, table) && table.Name == tableName && column.Name == columnName;
                    return true;
                }
                if (!(Relationships.SelectedItem is RelationshipChoice choice)) { error = "The model has no single-column relationship to profile."; return false; }
                var rel = choice.Relationship;
                if (rel.IsRemoved || rel.FromColumn == null || rel.ToColumn == null || rel.FromColumn.IsRemoved || rel.ToColumn.IsRemoved)
                { error = "The relationship or one of its columns is no longer available."; return false; }
                var coverage = CreateCoverageDraft(rel, limit);
                var from = rel.FromColumn; var to = rel.ToColumn;
                var stamp = Stamp(rel);
                Query = coverage.Query; RowLimit = coverage.RowLimit;
                IsCurrent = () => !rel.IsRemoved && ReferenceEquals(rel.FromColumn, from) && ReferenceEquals(rel.ToColumn, to) && Stamp(rel) == stamp;
                return true;
            }
            catch (ArgumentException ex) { error = "No draft created: " + ex.Message; return false; }
        }

        private static string Stamp(SingleColumnRelationship r) =>
            (r.FromColumn?.Table?.Name ?? "") + "\0" + (r.FromColumn?.Name ?? "") + "\0" + (r.ToColumn?.Table?.Name ?? "") + "\0" + (r.ToColumn?.Name ?? "");

        internal static ProfileValueKind KindOf(Column column)
        {
            switch (column.DataType)
            {
                case DataType.Int64: case DataType.Double: case DataType.Decimal: return ProfileValueKind.Numeric;
                case DataType.DateTime: return ProfileValueKind.DateTime;
                case DataType.String: return ProfileValueKind.Text;
                case DataType.Boolean: return ProfileValueKind.Boolean;
                default: return ProfileValueKind.Other;
            }
        }

        internal static DaxProfileDraft CreateColumnDraft(Column column, int topValues) =>
            DaxProfileQueryBuilder.ColumnProfile(column.Table.Name, column.Name, KindOf(column), topValues);

        internal static DaxProfileDraft CreateCoverageDraft(SingleColumnRelationship relationship, int sampleRows) =>
            DaxProfileQueryBuilder.RelationshipCoverage(relationship.FromColumn.Table.Name, relationship.FromColumn.Name,
                relationship.ToColumn.Table.Name, relationship.ToColumn.Name, sampleRows);

        private sealed class RelationshipChoice
        {
            internal RelationshipChoice(SingleColumnRelationship relationship) { Relationship = relationship; }
            internal SingleColumnRelationship Relationship { get; }
            public override string ToString() => Relationship.FromColumn.DaxObjectFullName + " → " + Relationship.ToColumn.DaxObjectFullName +
                (Relationship.IsActive ? "" : " (inactive)");
        }
    }
}
