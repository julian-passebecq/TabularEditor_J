using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using TabularEditor;
using TabularEditor.TOMWrapper;

internal static class Program
{
    private static readonly Assembly Host = typeof(FormMain).Assembly;

    [STAThread]
    private static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using (var handler = new TabularModelHandler())
            {
                var table = handler.Model.AddTable("Smoke model");
                table.AddMeasure("Base measure", "1");
                table.AddMeasure("Dependent measure", "[Base measure] + 1");
                AuthoringSmoke.Run(handler.Model);
                RecipeSmoke.Run(handler);
                SliceSmokes.RunEarly(handler);
                SnapshotPublisherSmoke.Run(handler);
                using (var recipeForm = new TabularEditor.PbiBench.Automation.PbiBenchRecipeForm(() => handler))
                { var handle = recipeForm.Handle; recipeForm.Show(); Application.DoEvents(); recipeForm.Close(); }
                using (var previewForm = new TabularEditor.PbiBench.Dax.PbiBenchTablePreviewForm(handler.Model))
                { var handle = previewForm.Handle; previewForm.Show(); Application.DoEvents(); previewForm.Close(); }
                var snapshotType = Host.GetType("TabularEditor.PbiBench.Semantic.PbiBenchSemanticSnapshot", true);
                var snapshot = snapshotType.GetMethod("Build").Invoke(null, new object[] { handler.Model });
                var index = snapshotType.GetProperty("Index").GetValue(snapshot);
                var descriptors = index.GetType().GetProperty("Descriptors").GetValue(index);

                CheckDialog("Navigation.PbiBenchQuickOpenForm", new[] { descriptors });
                CheckDialog("Semantic.PbiBenchSemanticViewForm", new[] { snapshot });
                CheckDialog("Dax.PbiBenchDaxWorkbenchForm", new object[] { null, "", "Offline smoke check" });
                using (var project = new TabularEditor.PbiBench.Project.PbiBenchProjectForm("Synthetic", false))
                { var handle = project.Handle; project.Show(); Application.DoEvents(); project.Close(); }
                DaxWorkbenchSmoke.Run(Host);
                DaxDocumentSmoke.Run(Host);
                QueryLifecycleSmoke.Run();
                MultipleResultsSmoke.Run();
                ProjectSmoke.Run();
                FormatterExportSmoke.Run();
                Rework01Smoke.Run();
                SliceSmokes.RunLate();
            }
            Console.WriteLine("PbiBench host smoke: PASS");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("PbiBench host smoke: FAIL");
            Console.Error.WriteLine(error);
            Console.Error.WriteLine("Base exception HRESULT: 0x" + error.GetBaseException().HResult.ToString("X8"));
            return 1;
        }
    }

    private static void CheckDialog(string name, object[] arguments)
    {
        var type = Host.GetType("TabularEditor.PbiBench." + name, true);
        using (var form = (Form)Activator.CreateInstance(type, arguments))
        {
            // Allocate native controls and exercise layout without displaying a window.
            var handle = form.Handle;
            CheckLayout(form);
            form.Size = form.MinimumSize;
            CheckLayout(form);
            form.Size = new Size(1400, 900);
            CheckLayout(form);
            form.Scale(new SizeF(1.5f, 1.5f));
            CheckLayout(form);
            form.Size = form.MinimumSize;
            CheckLayout(form);

            if (name == "Dax.PbiBenchDaxWorkbenchForm")
            {
                var execute = Descendants(form).OfType<Button>().Single(button => button.Text == "Execute");
                if (execute.Enabled) throw new InvalidOperationException("Offline queries must not be executable.");
            }
            Console.WriteLine(name + ": construction, resize and scaled layout PASS");
        }
    }

    private static void CheckLayout(Form form)
    {
        form.PerformLayout();
        foreach (var split in Descendants(form).OfType<SplitContainer>())
        {
            split.PerformLayout();
            int extent = split.Orientation == Orientation.Vertical ? split.ClientSize.Width : split.ClientSize.Height;
            if (split.SplitterDistance < split.Panel1MinSize ||
                extent - split.SplitterDistance - split.SplitterWidth < split.Panel2MinSize)
                throw new InvalidOperationException(form.Text + " has clipped split panels at " + form.Size);
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
