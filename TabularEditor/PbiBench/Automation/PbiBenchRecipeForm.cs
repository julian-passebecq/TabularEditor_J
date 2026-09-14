using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Automation;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Automation
{
    internal sealed class PbiBenchRecipeForm : Form
    {
        internal PbiBenchRecipeForm(Func<TabularModelHandler> currentHandler)
        {
            Text = "TE2 Enhanced · Reviewed metadata recipe";
            Size = new Size(900, 600); MinimumSize = new Size(650, 420);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var handler = currentHandler();
            var tables = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
            tables.Items.AddRange(handler.Model.Tables.ToArray());
            var operation = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            operation.Items.AddRange(new object[] { MetadataRecipeOperation.SetDisplayFolder, MetadataRecipeOperation.SetHidden });
            var folder = new TextBox { Width = 200, MaxLength = 1024 };
            var hidden = new CheckBox { Text = "Hidden", AutoSize = true };
            var preview = new Button { Text = "Preview", AutoSize = true };
            var apply = new Button { Text = "Apply reviewed changes", AutoSize = true, Enabled = false };
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 75, AutoScroll = true };
            top.Controls.AddRange(new Control[] { tables, operation, folder, hidden, preview, apply });
            var rows = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
            rows.Columns.Add("Measure", 300); rows.Columns.Add("Before", 240); rows.Columns.Add("After", 240);
            var status = new Label { Dock = DockStyle.Bottom, Height = 70,
                Text = "Restricted lane: only display folder or hidden state for the selected table's measures.\r\nPreview exact changes, then apply through TE2 undo. No C# is compiled or executed. No disk/live save." };
            Controls.Add(rows); Controls.Add(top); Controls.Add(status);
            PbiBenchMetadataRecipe recipe = null;
            Action invalidate = () => { recipe = null; apply.Enabled = false; rows.Items.Clear(); };
            tables.SelectedIndexChanged += (s, e) => invalidate();
            operation.SelectedIndexChanged += (s, e) => { invalidate(); folder.Enabled = operation.SelectedIndex == 0; hidden.Enabled = operation.SelectedIndex == 1; };
            folder.TextChanged += (s, e) => invalidate(); hidden.CheckedChanged += (s, e) => invalidate();
            operation.SelectedIndex = 0;
            if (tables.Items.Count > 0) tables.SelectedIndex = 0;
            preview.Click += (s, e) => {
                invalidate();
                if (!(tables.SelectedItem is Table table) || table.IsRemoved || !ReferenceEquals(handler, currentHandler()))
                { status.Text = "Model or table changed. Reopen the recipe window."; return; }
                try {
                    recipe = new PbiBenchMetadataRecipe(handler, table.Measures, (MetadataRecipeOperation)operation.SelectedItem,
                        operation.SelectedIndex == 0 ? folder.Text : hidden.Checked.ToString());
                    foreach (var change in recipe.Plan.Changes)
                    {
                        var item = new ListViewItem(change.Id); item.SubItems.Add(change.Before); item.SubItems.Add(change.After); rows.Items.Add(item);
                    }
                    apply.Enabled = recipe.Plan.Changes.Count > 0;
                    status.Text = recipe.Plan.Changes.Count + " exact changes. Review Before/After before Apply. One TE2 undo batch.\r\nStale measure metadata or replaced objects reject apply. Maximum 1,000 target measures.";
                }
                catch (Exception) { recipe = null; status.Text = "Cannot propose this recipe: check model availability and scope/value limits."; }
            };
            apply.Click += (s, e) => {
                if (recipe == null) return;
                apply.Enabled = false;
                try { recipe.Apply(currentHandler()); status.Text = "Applied to the loaded model only. Use TE2 Undo to revert; no model save was performed."; }
                catch (Exception) { status.Text = "Apply rejected or failed. Preview again if stale. Property failures attempt TE2 rollback; inspect model/Undo before saving. No disk save was performed."; }
                finally { recipe = null; }
            };
        }
    }
}
