using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PbiBench.Core.Automation;
using TabularEditor;
using TabularEditor.PbiBench.Automation;
using TabularEditor.TOMWrapper;

// S4 Automation host smokes: reviewed recipes (format string, description, rename) and the C# script workspace.
internal static partial class SliceSmokes
{
    static partial void EarlyAutomation(TabularModelHandler handler)
    {
        RecipeFormatStringSmoke(handler);
        RecipeRenameSmoke(handler);
        ScriptWorkspaceSmoke(handler);
    }

    private static void RecipeFormatStringSmoke(TabularModelHandler handler)
    {
        var table = handler.Model.AddTable("Format synthetic");
        var a = table.AddMeasure("Amount A", "1");
        var b = table.AddMeasure("Amount B", "2");
        b.FormatString = "0.0%";
        var depth = handler.UndoManager.BatchDepth;
        var recipe = new PbiBenchMetadataRecipe(handler, table.Measures, MetadataRecipeOperation.SetFormatString, "#,0.00");
        AutoHostCheck(recipe.Plan.Changes.Count == 2 && recipe.Plan.Changes.All(c => c.Property == "FormatString") && a.FormatString == "", "Format preview wrong or mutated the model.");
        recipe.Apply(handler);
        AutoHostCheck(a.FormatString == "#,0.00" && b.FormatString == "#,0.00" && handler.UndoManager.BatchDepth == depth, "Format apply incomplete.");
        handler.UndoManager.Undo();
        AutoHostCheck(a.FormatString == "" && b.FormatString == "0.0%", "Format apply must undo in one step.");
        AutoHostReject(() => new PbiBenchMetadataRecipe(handler, table.Measures, MetadataRecipeOperation.SetFormatString, "\"$#,0"));

        var description = new PbiBenchMetadataRecipe(handler, new[] { a }, MetadataRecipeOperation.SetDescription, "Synthetic\r\nline two");
        b.Description = "external edit";
        AutoHostReject(() => description.Apply(handler));
        AutoHostCheck(a.Description == "", "Stale description plan partially applied.");
        description = new PbiBenchMetadataRecipe(handler, new[] { a }, MetadataRecipeOperation.SetDescription, "Synthetic\r\nline two");
        description.Apply(handler);
        AutoHostCheck(a.Description == "Synthetic\nline two" && description.Plan.Changes[0].After == a.Description, "Description apply failed (TE2 stores LF).");
        using (var recipeForm = new PbiBenchRecipeForm(() => handler)) { var h = recipeForm.Handle; recipeForm.Show(); Application.DoEvents(); AutoLayoutCheck(recipeForm); recipeForm.Close(); }
        table.Delete();
        Console.WriteLine("RecipeFormatStringSmoke: format/description preview, apply, single undo, validator and stale rejection PASS");
    }

    private static void RecipeRenameSmoke(TabularModelHandler handler)
    {
        var settings = handler.Settings;
        var fixup = settings.AutoFixup;
        settings.AutoFixup = true;
        try
        {
            var table = handler.Model.AddTable("Rename synthetic");
            table.AddDataColumn("Amount");
            var sales = table.AddMeasure("Sales", "SUM('Rename synthetic'[Amount])");
            var margin = table.AddMeasure("Margin", "[Sales] * 2");
            var qualified = table.AddMeasure("Qualified", "'Rename synthetic'[Sales]\r\n + [Sales]");
            var marginBefore = margin.Expression;
            var qualifiedBefore = qualified.Expression;

            var recipe = new PbiBenchMetadataRecipe(handler, new[] { sales }, MetadataRecipeOperation.Rename, "Net Sales");
            var rewrites = recipe.Plan.DependentRewrites.ToDictionary(r => r.DependentId, r => r.After);
            AutoHostCheck(sales.Name == "Sales" && margin.Expression == marginBefore, "Rename preview mutated the model.");
            AutoHostCheck(rewrites.Count == 2 && rewrites.Values.Contains("[Net Sales] * 2") && rewrites.Values.Contains("[Net Sales]\n + [Net Sales]"),
                "Rename preview does not list the expected dependent rewrites: " + string.Join(" | ", rewrites.Values));

            recipe.Apply(handler); // Apply itself verifies TE2 FormulaFixup produced exactly the previewed expressions.
            AutoHostCheck(sales.Name == "Net Sales" && margin.Expression == "[Net Sales] * 2" && qualified.Expression == "[Net Sales]\n + [Net Sales]", "Rename apply/fix-up mismatch.");
            handler.UndoManager.Undo();
            AutoHostCheck(sales.Name == "Sales" && margin.Expression == marginBefore && qualified.Expression == qualifiedBefore, "Rename must undo name and rewrites in one step.");

            var stale = new PbiBenchMetadataRecipe(handler, new[] { sales }, MetadataRecipeOperation.Rename, "Net Sales");
            margin.Expression = "[Sales] * 3";
            AutoHostReject(() => stale.Apply(handler));
            AutoHostCheck(sales.Name == "Sales", "Stale rename applied.");

            AutoHostReject(() => new PbiBenchMetadataRecipe(handler, new[] { sales }, MetadataRecipeOperation.Rename, "amount"));
            AutoHostReject(() => new PbiBenchMetadataRecipe(handler, new[] { sales }, MetadataRecipeOperation.Rename, "margin"));
            var label = table.AddMeasure("Label", "\"[Sales]\" & FORMAT([Sales], \"0\")");
            AutoHostReject(() => new PbiBenchMetadataRecipe(handler, new[] { sales }, MetadataRecipeOperation.Rename, "Net Sales"));
            label.Delete();
            settings.AutoFixup = false;
            AutoHostReject(() => new PbiBenchMetadataRecipe(handler, new[] { sales }, MetadataRecipeOperation.Rename, "Net Sales"));
            table.Delete();
        }
        finally { settings.AutoFixup = fixup; }
        Console.WriteLine("RecipeRenameSmoke: dependent preview equals TE2 fix-up, single undo, stale/collision/untracked/fix-up-off refusals PASS");
    }

    private static void ScriptWorkspaceSmoke(TabularModelHandler handler)
    {
        // Same initialisation TE2 performs at startup (registers script helper methods, ensures the TOMWrapper copy it compiles against).
        ScriptEngine.InitScriptEngine(new List<Assembly>());
        var root = Path.Combine(Path.GetTempPath(), "pbibench-s4-recovery-" + Guid.NewGuid().ToString("N"));
        var table = handler.Model.AddTable("Script synthetic");
        var current = handler;
        try
        {
            using (var form = new PbiBenchScriptWorkspaceForm(() => current, () => null, root))
            {
                var handle = form.Handle;
                var confirmations = 0;
                Func<PbiBench.Core.Automation.ScriptDocument, IReadOnlyList<ScriptDiagnostic>, bool> yes = (d, a) => { confirmations++; return true; };
                Func<PbiBench.Core.Automation.ScriptDocument, IReadOnlyList<ScriptDiagnostic>, bool> no = (d, a) => { confirmations++; return false; };
                AutoHostCheck(form.Workspace.Documents.Count == 1 && form.ActiveDocument != null && !form.ActiveDocument.IsDirty, "Workspace must open with one clean starter tab.");

                form.SetActiveText("var x = ;");
                var broken = form.CompileActive();
                AutoHostCheck(broken != null && !broken.Succeeded && form.Problems.Items.Count > 0 && broken.Diagnostics[0].Line == 1, "Compile errors must reach the Problems list: " + form.StatusText);
                AutoHostCheck(form.RunActive(yes) == null && confirmations == 0, "Scripts with compile errors must not run or ask for trust.");

                var doc = form.NewDocument("Model.Tables[\"Script synthetic\"].AddMeasure(\"Made by script\", \"1\");\r\nModel.Tables[\"Script synthetic\"].AddMeasure(\"Second\", \"2\");");
                AutoHostCheck(form.RunActive(no) == null && confirmations == 1 && table.Measures.Count == 0, "Declined trust must not run.");
                var outcome = form.RunActive(yes);
                AutoHostCheck(outcome != null && outcome.State == PbiBenchScriptRunState.Succeeded && table.Measures.Count == 2 && doc.IsTrusted, "Trusted run failed: " + form.StatusText);
                handler.UndoManager.Undo();
                AutoHostCheck(table.Measures.Count == 0, "Script changes must undo as one TE2 step.");
                outcome = form.RunActive(yes);
                AutoHostCheck(confirmations == 2 && outcome.State == PbiBenchScriptRunState.Succeeded, "Unchanged trusted script must not ask again.");
                handler.UndoManager.Undo();

                form.SetActiveText(doc.Text + "\r\n// edited");
                AutoHostCheck(!doc.IsTrusted, "Edit must revoke trust.");
                var old = PbiBenchScriptCompiler.Compile(doc);
                form.SetActiveText(doc.Text + " ");
                AutoHostThrows(() => PbiBenchScriptCompiler.Run(handler, doc, old, null, true), "Stale compilation must be refused.");

                form.SetActiveText("Model.Tables[\"Script synthetic\"].AddMeasure(\"Partial\", \"1\");\r\nthrow new System.InvalidOperationException(\"synthetic failure\");");
                outcome = form.RunActive(yes);
                AutoHostCheck(confirmations == 3 && outcome.State == PbiBenchScriptRunState.Failed && outcome.RolledBack && outcome.Line == 2 && table.Measures.Count == 0 &&
                    handler.UndoManager.BatchDepth == 0, "Failing script must roll back: " + form.StatusText);

                current = null;
                AutoHostCheck(form.RunActive(yes) == null && form.StatusText.Contains("model changed"), "Run against a different model must be refused.");
                current = handler;

                AutoHostCheck(!form.SaveRecoveryNow() && !Directory.Exists(root), "Recovery must stay off without opt-in.");
                form.Show(); Application.DoEvents();
                AutoLayoutCheck(form);
                form.Hide();
            }
            File.WriteAllText(root + ".optin", "synthetic opt-in");
            using (var form = new PbiBenchScriptWorkspaceForm(() => current, () => null, root))
            {
                var doc = form.NewDocument("// synthetic recovery text");
                AutoHostCheck(form.SaveRecoveryNow() && form.Recovery.Read(doc.Id) == "// synthetic recovery text", "Opted-in recovery copy missing.");
            }
        }
        finally
        {
            table.Delete();
            try { if (Directory.Exists(root)) Directory.Delete(root, true); if (File.Exists(root + ".optin")) File.Delete(root + ".optin"); } catch (IOException) { }
        }
        Console.WriteLine("ScriptWorkspaceSmoke: Problems list, per-version trust, one undo batch, rollback on failure, stale/model guards, opt-in recovery PASS");
    }

    private static void AutoLayoutCheck(Form form)
    {
        foreach (var size in new[] { form.MinimumSize, new System.Drawing.Size(1400, 900) })
        {
            form.Size = size; form.PerformLayout();
            foreach (var split in AutoDescendants(form).OfType<SplitContainer>())
            {
                split.PerformLayout();
                var extent = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
                AutoHostCheck(split.SplitterDistance >= split.Panel1MinSize && extent - split.SplitterDistance - split.SplitterWidth >= split.Panel2MinSize,
                    form.Text + " clips split panels at " + size);
            }
        }
        form.Scale(new System.Drawing.SizeF(1.5f, 1.5f)); form.PerformLayout();
        form.Size = form.MinimumSize; form.PerformLayout();
    }

    private static IEnumerable<Control> AutoDescendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var d in AutoDescendants(child)) yield return d;
        }
    }

    private static void AutoHostCheck(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AutoHostReject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected recipe rejection.");
    }

    private static void AutoHostThrows(Action action, string message)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }
}
