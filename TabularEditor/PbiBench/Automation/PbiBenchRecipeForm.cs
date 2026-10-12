using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Automation;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Automation
{
    /// <summary>
    /// Restricted reviewed-recipe lane (AUTO-002): allowlisted measure properties from <see cref="RecipeCatalog"/>, typed
    /// validation, exact per-property Before/After preview (plus dependent expressions for rename), stale rejection and
    /// one TE2 undo batch. No C# is compiled or executed here.
    /// </summary>
    internal sealed class PbiBenchRecipeForm : Form
    {
        private const string AllMeasures = "(all measures in table)";

        internal PbiBenchRecipeForm(Func<TabularModelHandler> currentHandler)
        {
            Text = "TE2 Enhanced · Reviewed metadata recipe";
            Size = new Size(1000, 680); MinimumSize = new Size(700, 480);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var handler = currentHandler();
            var tables = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "Name" };
            tables.Items.AddRange(handler.Model.Tables.ToArray());
            var recipes = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
            recipes.Items.AddRange(RecipeCatalog.All.Cast<object>().ToArray());
            var measures = new ComboBox { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
            var value = new TextBox { Width = 240, MaxLength = 4000 };
            var hidden = new CheckBox { Text = "Hidden", AutoSize = true };
            var preview = new Button { Text = "Preview", AutoSize = true };
            var apply = new Button { Text = "Apply reviewed changes", AutoSize = true, Enabled = false };
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
            top.Controls.AddRange(new Control[] { tables, recipes, measures, value, hidden, preview, apply });
            var rows = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
            rows.Columns.Add("Object", 230); rows.Columns.Add("Property", 130); rows.Columns.Add("Before", 280); rows.Columns.Add("After", 280);
            var details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font(FontFamily.GenericMonospace, 9f) };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Panel1MinSize = 100, Panel2MinSize = 60 };
            split.Panel1.Controls.Add(rows); split.Panel2.Controls.Add(details);
            var status = new Label { Dock = DockStyle.Bottom, Height = 70, AutoEllipsis = true };
            Controls.Add(split); Controls.Add(top); Controls.Add(status);
            Load += (s, e) => { if (split.Height > split.Panel1MinSize + split.Panel2MinSize + split.SplitterWidth) split.SplitterDistance = Math.Max(split.Panel1MinSize, split.Height * 2 / 3); };

            PbiBenchMetadataRecipe recipe = null;
            Action invalidate = () => { recipe = null; apply.Enabled = false; rows.Items.Clear(); details.Text = ""; };
            Func<RecipeDefinition> definition = () => recipes.SelectedItem as RecipeDefinition;
            Action describe = () =>
            {
                var d = definition();
                status.Text = d == null ? "" : d.Title + ": " + d.Help + "\r\nPreview exact changes, then apply through one TE2 undo step. No C# is compiled or executed. No disk/live save.";
            };
            Action refreshInputs = () =>
            {
                var d = definition();
                var table = tables.SelectedItem as Table;
                measures.Items.Clear();
                if (d != null && !d.SingleTargetOnly) measures.Items.Add(AllMeasures);
                if (table != null && !table.IsRemoved) measures.Items.AddRange(table.Measures.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
                measures.DisplayMember = "Name";
                if (measures.Items.Count > 0) measures.SelectedIndex = 0;
                var boolean = d?.ValueKind == RecipeValueKind.Boolean;
                hidden.Enabled = boolean; value.Enabled = !boolean;
                value.Multiline = d?.ValueKind == RecipeValueKind.MultilineText;
                value.Height = value.Multiline ? 60 : value.PreferredHeight;
                value.MaxLength = d?.MaxLength ?? 1024;
                describe();
            };
            tables.SelectedIndexChanged += (s, e) => { invalidate(); refreshInputs(); };
            recipes.SelectedIndexChanged += (s, e) => { invalidate(); refreshInputs(); };
            measures.SelectedIndexChanged += (s, e) => invalidate();
            value.TextChanged += (s, e) => invalidate(); hidden.CheckedChanged += (s, e) => invalidate();
            rows.SelectedIndexChanged += (s, e) =>
            {
                details.Text = rows.SelectedItems.Count == 1 && rows.SelectedItems[0].Tag is string[] pair
                    ? "BEFORE" + Environment.NewLine + pair[0] + Environment.NewLine + Environment.NewLine + "AFTER" + Environment.NewLine + pair[1] : "";
            };
            recipes.SelectedIndex = 0;
            if (tables.Items.Count > 0) tables.SelectedIndex = 0;

            preview.Click += (s, e) =>
            {
                invalidate();
                var d = definition();
                if (d == null || !(tables.SelectedItem is Table table) || table.IsRemoved || !ReferenceEquals(handler, currentHandler()))
                { status.Text = "Model or table changed. Reopen the recipe window."; return; }
                var targets = measures.SelectedItem is Measure single ? new[] { single } : table.Measures.ToArray();
                if (targets.Any(m => m.IsRemoved)) { status.Text = "A measure was removed. Reopen the recipe window."; return; }
                var input = d.ValueKind == RecipeValueKind.Boolean ? hidden.Checked.ToString() : value.Text;
                try
                {
                    recipe = new PbiBenchMetadataRecipe(handler, targets, d.Operation, input);
                    foreach (var change in recipe.Plan.Changes) AddRow(rows, change.Id, change.Property, change.Before, change.After);
                    foreach (var rewrite in recipe.Plan.DependentRewrites) AddRow(rows, rewrite.DependentId, rewrite.Property, rewrite.Before, rewrite.After);
                    apply.Enabled = recipe.Plan.Changes.Count > 0;
                    status.Text = recipe.Plan.Changes.Count + " property change(s)" +
                        (d.Operation == MetadataRecipeOperation.Rename ? ", " + recipe.Plan.DependentRewrites.Count + " dependent expression(s) TE2 will rewrite (line endings become LF)" : "") +
                        ". Review Before/After, then Apply as one TE2 undo step.\r\nStale metadata or replaced objects reject apply. Report visuals and external tools are not rewritten. Maximum 1,000 target measures.";
                }
                catch (ArgumentException ex) { recipe = null; status.Text = "Cannot propose this recipe: " + ex.Message; }
                catch (InvalidOperationException ex) { recipe = null; status.Text = "Cannot propose this recipe: " + ex.Message; }
            };
            apply.Click += (s, e) =>
            {
                if (recipe == null) return;
                apply.Enabled = false;
                try { recipe.Apply(currentHandler()); status.Text = "Applied to the loaded model only. Use TE2 Undo to revert; no model save was performed."; }
                catch (Exception ex)
                { status.Text = "Apply rejected or failed (" + ex.Message + ") Preview again if stale. TE2 rolled the batch back; inspect model/Undo before saving. No disk save was performed."; }
                finally { recipe = null; }
            };
        }

        private static void AddRow(ListView rows, string id, string property, string before, string after)
        {
            var item = new ListViewItem(id) { Tag = new[] { before, after } };
            item.SubItems.Add(property); item.SubItems.Add(OneLine(before)); item.SubItems.Add(OneLine(after));
            rows.Items.Add(item);
        }

        private static string OneLine(string text)
        {
            var flat = (text ?? "").Replace("\r", "").Replace('\n', ' ');
            return flat.Length > 200 ? flat.Substring(0, 200) + "…" : flat;
        }
    }
}
