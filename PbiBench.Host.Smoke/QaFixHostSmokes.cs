using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using TabularEditor.PbiBench.Automation;
using TabularEditor.PbiBench.Dax;
using TabularEditor.PbiBench.Explore;
using TabularEditor.PbiBench.Navigation;
using TabularEditor.PbiBench.Project;
using TabularEditor.TOMWrapper;

/// <summary>Host regression checks for the 2026-10-13 Light QA packet: D2 layout, D14 Esc/keyboard exit, D15 offline F5, D17 starter text and focus.</summary>
internal static class QaFixHostSmokes
{
    /// <summary>D2: every Project toolbar control stays inside the client area at the minimum size and at 1400 px wide.</summary>
    internal static void ProjectLayout()
    {
        using (var form = new PbiBenchProjectForm("Synthetic", false) { CheckSaveTarget = () => null })
        {
            form.Show(); Application.DoEvents();
            foreach (var size in new[] { form.MinimumSize, new Size(1400, 900), new Size(form.MinimumSize.Width, 900) })
            {
                form.Size = size; form.PerformLayout(); Application.DoEvents();
                var bar = form.CommandBar;
                var client = form.ClientRectangle;
                var visible = bar.Controls.Cast<Control>().Where(c => c.Visible).ToArray();
                Check(visible.Length >= 10, "D2 all project commands visible (" + visible.Length + ")");
                foreach (var control in visible)
                {
                    var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                    Check(client.Contains(bounds), "D2 '" + control.Text + "' inside the Project window at " + form.Size + " (" + bounds + " in " + client + ")");
                }
            }
            // D14: Esc closes the Project window.
            PressEscape(form);
            Check(form.IsDisposed || !form.Visible, "D14 Esc closes Project");
        }
        Console.WriteLine("QA fixes D2/D14 Project window: layout at minimum and 1400 px, Esc PASS");
    }

    /// <summary>D14 Esc in Recipes / Table preview / Profile draft; D14 Ctrl+M editor exit; D15 offline F5; D17 starter text and focus return.</summary>
    internal static void Dialogs(TabularModelHandler handler)
    {
        var table = handler.Model.AddTable("QA Esc table");
        table.AddDataColumn("Key", "Key", null, DataType.Int64);
        try
        {
            foreach (var create in new Func<Form>[]
            {
                () => new PbiBenchRecipeForm(() => handler),
                () => new PbiBenchTablePreviewForm(handler.Model),
                () => new PbiBenchProfileForm(handler.Model)
            })
            {
                using (var form = create())
                {
                    form.Show(); Application.DoEvents();
                    var name = form.GetType().Name;
                    PressEscape(form);
                    Application.DoEvents();
                    Check(form.IsDisposed || !form.Visible, "D14 Esc closes " + name);
                }
            }
        }
        finally { if (!table.IsRemoved) table.Delete(); }

        // DAX Workbench offline: untouched starter and generated drafts are clean; F5 explains; Ctrl+M toggles Tab focus.
        using (var workbench = new PbiBenchDaxWorkbenchForm(null, "", "Offline smoke check"))
        {
            workbench.Show(); Application.DoEvents();
            Check(workbench.Tabs.All(t => !t.Document.IsDirty), "D17 untouched Workbench starter is not dirty");
            workbench.LoadPreviewDraft("EVALUATE TOPN(10, 'QA')", 10);
            Application.DoEvents();
            Check(workbench.Tabs.All(t => !t.Document.IsDirty), "D17 untouched generated draft is not dirty");
            Check(Key(workbench, Keys.F5) && workbench.StatusText.Contains("live connection"), "D15 offline F5 explains: " + workbench.StatusText);
            Check(Key(workbench, Keys.Control | Keys.M) && workbench.TabMovesFocus && workbench.Tabs.All(t => !t.Editor.AcceptsTab), "D14 Ctrl+M: Tab leaves the DAX editor");
            var added = workbench.NewTab();
            Check(added != null && !added.Editor.AcceptsTab, "D14 new tab follows Tab-moves-focus");
            Check(Key(workbench, Keys.Control | Keys.Tab) && workbench.TabMovesFocus, "D14 Ctrl+Tab still switches tabs");
            Check(Key(workbench, Keys.Control | Keys.M) && !workbench.TabMovesFocus && workbench.Tabs.All(t => t.Editor.AcceptsTab), "D14 Ctrl+M toggles back");
            Check(workbench.Tabs.All(t => !t.Document.IsDirty), "D17 nothing dirty before close");
            workbench.Close();
            Check(workbench.IsDisposed || !workbench.Visible, "D17 Workbench closes without a prompt");
        }

        // Script workspace: untouched starter is clean after the editor handle exists; Ctrl+M toggles Tab focus.
        var root = Path.Combine(Path.GetTempPath(), "pbibench-qa-script-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var scripts = new PbiBenchScriptWorkspaceForm(() => handler, () => null, root))
            {
                scripts.Show(); Application.DoEvents();
                Check(scripts.Workspace.Documents.All(d => !d.IsDirty), "D17 untouched script starter is not dirty");
                Check(Key(scripts, Keys.Control | Keys.M) && scripts.TabMovesFocus, "D14 Ctrl+M in the script workspace");
                var editors = Descendants(scripts).OfType<TextBox>().Where(t => t.Multiline && !t.ReadOnly).ToArray();
                Check(editors.Length > 0 && editors.All(t => !t.AcceptsTab), "D14 Tab leaves the script editor");
                Key(scripts, Keys.Control | Keys.M);
                scripts.Close();
                Check(scripts.IsDisposed || !scripts.Visible, "D17 script workspace closes without a prompt");
            }
        }
        finally { try { Directory.Delete(root, true); } catch (Exception) { } }

        // D17 focus return: a previously focused control gets focus back; a menu strip falls back to the model tree.
        using (var owner = new Form { Size = new Size(400, 300), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000) })
        {
            var menu = new MenuStrip(); menu.Items.Add("File");
            var tree = new TreeView { Dock = DockStyle.Left };
            var box = new TextBox { Dock = DockStyle.Top };
            owner.Controls.Add(tree); owner.Controls.Add(box); owner.Controls.Add(menu); owner.MainMenuStrip = menu;
            owner.Show(); owner.Activate(); Application.DoEvents();
            // A service-hosted runner (no interactive desktop) drops the first SetFocus on a fresh window: start from the tree so
            // the check measures a real move tree -> box instead of window activation.
            tree.Focus(); Application.DoEvents();
            PbiBenchDialogKeys.RestoreFocus(box, tree); Application.DoEvents();
            var boxFocused = box.Focused;
            PbiBenchDialogKeys.RestoreFocus(menu, tree); Application.DoEvents();
            var treeFocused = tree.Focused;
            PbiBenchDialogKeys.RestoreFocus(null, tree); Application.DoEvents();
            Check(boxFocused && treeFocused && tree.Focused, "D17 focus returns to the previous control or the model tree (box " + boxFocused + ", tree " + treeFocused + ")");
        }
        Console.WriteLine("QA fixes D14/D15/D17: Esc in 3 dialogs, Ctrl+M editor exit, offline F5 status, clean starters, focus return PASS");
    }

    private static void PressEscape(Form form) => Key(form, Keys.Escape);

    private static bool Key(Form form, Keys keys)
    {
        var method = typeof(Form).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic);
        var args = new object[] { Message.Create(form.Handle, 0x0100, (IntPtr)(int)(keys & Keys.KeyCode), IntPtr.Zero), keys };
        var handled = (bool)method.Invoke(form, args);
        Application.DoEvents();
        return handled;
    }

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception("QA fix host: " + message); }
}
