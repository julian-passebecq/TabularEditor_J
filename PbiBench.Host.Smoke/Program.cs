using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Forms;
using TabularEditor;
using TabularEditor.TOMWrapper;

internal static class Program
{
    private static readonly Assembly Host = typeof(FormMain).Assembly;

    [STAThread]
    private static int Main(string[] args)
    {
        // The portable host embeds TOMWrapper/Newtonsoft with its existing Costura module initializer.
        // Run that exact host initializer before JIT touches TOM types; never copy shadow DLLs into a package.
        RuntimeHelpers.RunModuleConstructor(Host.ManifestModule.ModuleHandle);
        return Run(args);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length != 0)
            {
                if (args.Length != 2 || args[0] != "--galaxy-live-only" || !Path.IsPathRooted(args[1])) throw new ArgumentException("Only --galaxy-live-only <absolute extracted portable root> is supported.");
                var portable = Path.GetFullPath(args[1]).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var testRoot = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (!string.Equals(portable, testRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(Path.Combine(portable, "MANIFEST.txt"))) throw new InvalidOperationException("Copy only this exact-SHA harness/config into an owned extracted portable root containing MANIFEST.txt.");
                foreach (var assembly in new[] { Host, typeof(TabularModelHandler).Assembly, typeof(PbiBench.Core.Interop.Te2LiveBridgeV1).Assembly, typeof(Newtonsoft.Json.Linq.JObject).Assembly })
                {
                    if (!string.IsNullOrEmpty(assembly.Location))
                    {
                        if (!string.Equals(Path.GetDirectoryName(assembly.Location), portable, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("PACKAGED_ASSEMBLY_REQUIRED: " + assembly.GetName().Name);
                        PrintQualifiedFile("loaded assembly", assembly.Location);
                    }
                    else PrintEmbeddedAssembly(assembly);
                }
                var companion = Path.Combine(portable, "mcp", "TE2Enhanced.Mcp.exe");
                PrintQualifiedFile("selected packaged stdio companion", companion);
                PrintQualifiedFile("selected packaged stdio implementation", Path.ChangeExtension(companion, ".dll"));
                using (var handler = new TabularModelHandler()) Te2LiveBridgeSmoke.Run(handler, companion);
                Console.WriteLine("Galaxy live-only PACKAGED host smoke: PASS; full Verify was NOT_RUN by this opt-in mode; no human Save/engine/Fabric acceptance claimed.");
                return 0;
            }
            using (var handler = new TabularModelHandler())
            {
                var table = handler.Model.AddTable("Smoke model");
                table.AddMeasure("Base measure", "1");
                table.AddMeasure("Dependent measure", "[Base measure] + 1");
                AuthoringSmoke.Run(handler.Model);
                RecipeSmoke.Run(handler);
                SliceSmokes.RunEarly(handler);
                SnapshotPublisherSmoke.Run(handler);
                Te2LiveBridgeSmoke.Run(handler);
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
                CredentialCloneSmoke.Run();
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

    private static void PrintQualifiedFile(string label, string path)
    {
        using (var input = File.OpenRead(path))
        using (var sha = SHA256.Create()) Console.WriteLine("Galaxy portable " + label + ": " + path + " ; sha256=" + BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant());
    }

    private static void PrintEmbeddedAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name;
        if (name != "TOMWrapper" && name != "Newtonsoft.Json") throw new InvalidOperationException("UNEXPECTED_MEMORY_ASSEMBLY: " + name);
        var resource = "costura." + name.ToLowerInvariant() + ".dll.compressed";
        using (var source = Host.GetManifestResourceStream(resource))
        {
            if (source == null) throw new InvalidOperationException("PACKAGED_EMBEDDED_RESOURCE_REQUIRED: " + resource);
            using (var inflate = new DeflateStream(source, CompressionMode.Decompress))
            using (var bytes = new MemoryStream())
            {
                inflate.CopyTo(bytes);
                var content = bytes.ToArray();
                var identity = Assembly.ReflectionOnlyLoad(content);
                if (identity.FullName != assembly.FullName || identity.ManifestModule.ModuleVersionId != assembly.ManifestModule.ModuleVersionId) throw new InvalidOperationException("EMBEDDED_ASSEMBLY_IDENTITY_MISMATCH: " + name);
                using (var sha = SHA256.Create()) Console.WriteLine("Galaxy portable loaded embedded assembly: Location=<memory>; host=" + Host.Location + "; resource=" + resource + "; mvid=" + assembly.ManifestModule.ModuleVersionId + "; sha256=" + BitConverter.ToString(sha.ComputeHash(content)).Replace("-", "").ToLowerInvariant());
            }
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
