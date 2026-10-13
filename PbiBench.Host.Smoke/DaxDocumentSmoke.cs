using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;

internal static class DaxDocumentSmoke
{
    public static void Run(Assembly host)
    {
        var directory = Path.Combine(Path.GetTempPath(), "pbibench-documents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckFiles(directory);
            CheckWorkbench(host, directory);
            Console.WriteLine("DAX documents: files, conflicts, dirty state, cancelled dialogs and replacement guards PASS");
        }
        finally
        {
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }

    private static void CheckFiles(string directory)
    {
        var path = Path.Combine(directory, "unicode.dax");
        const string text = "// café 東京\r\nEVALUATE ROW(\"Value\", 1)";
        File.WriteAllText(path, text, new UTF8Encoding(true));
        var document = new DaxDocument();
        document.Open(path);
        Assert(document.Text == text && !document.IsDirty, "UTF-8 BOM document was not loaded correctly.");
        document.Text = text.Replace("\r\n", "\n");
        Assert(!document.IsDirty, "RichTextBox newline normalization marked the document dirty.");
        document.Text += "\n// edited";
        File.WriteAllText(path, "// external edit");
        ExpectFailure(() => document.Save(path));
        Assert(File.ReadAllText(path) == "// external edit" && document.IsDirty, "External edit was overwritten or draft lost.");

        var copy = Path.Combine(directory, "copy.dax");
        document.Save(copy);
        Assert(!document.IsDirty && File.ReadAllText(copy) == document.Text, "Save As did not preserve Unicode edits.");
        document.Text += "\n// second save";
        document.Save(copy);
        Assert(File.ReadAllText(copy) == document.Text, "Atomic replacement failed.");
        Assert(Directory.GetFiles(directory, "*.tmp-*").Length == 0, "Save left temporary files behind.");
        document.Text += "\n// unsaved";
        ExpectFailure(() => document.Save(Path.Combine(directory, "missing", "fail.dax")));
        Assert(document.FilePath == copy && document.IsDirty, "Failed Save As changed document identity.");

        var invalid = Path.Combine(directory, "invalid.dax");
        File.WriteAllBytes(invalid, new byte[] { 0xff, 0xfe, 0, 0, 0, 0, 0, 0 });
        var previous = document.Text;
        ExpectFailure(() => document.Open(invalid));
        Assert(document.Text == previous && document.FilePath == copy, "Failed Open replaced the current draft.");
        File.WriteAllText(invalid, "// Valid UTF-16 text is still outside the UTF-8 contract", Encoding.Unicode);
        ExpectFailure(() => document.Open(invalid));
        File.WriteAllBytes(invalid, new byte[] { 0xc3, 0x28 });
        ExpectFailure(() => document.Open(invalid));
        using (var stream = File.Create(invalid)) stream.SetLength(DaxDocument.MaxFileBytes + 1L);
        ExpectFailure(() => document.Open(invalid));
        Assert(document.Text == previous, "Oversized file replaced the draft.");
        File.Delete(copy);
        ExpectFailure(() => document.Save(copy));
        Assert(!File.Exists(copy), "Externally deleted file was silently recreated.");
    }

    private static void CheckWorkbench(Assembly host, string directory)
    {
        var dialogs = new Dialogs();
        var type = host.GetType("TabularEditor.PbiBench.Dax.PbiBenchDaxWorkbenchForm", true);
        using (var form = (Form)Activator.CreateInstance(type, null, "", "Offline", dialogs))
        {
            var formHandle = form.Handle;
            var editor = Field<RichTextBox>(form, "_queryEditor");
            var editorHandle = editor.Handle;
            var document = Field<DaxDocument>(form, "_document");
            editor.Text = "EVALUATE ROW(\"Draft\", 1)";
            Assert(form.Text.Contains(" *") && document.IsDirty, "Edits did not mark the title dirty.");
            var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
            Invoke(form, "OnWorkbenchClosing", form, closing);
            Assert(closing.Cancel && !Field<bool>(form, "_closing"), "Cancel did not preserve the open draft.");
            Invoke(form, "NewDocument");
            Assert(document.IsDirty, "Cancelled New discarded the draft.");

            dialogs.Answer = DialogResult.Yes;
            Invoke(form, "NewDocument");
            Assert(document.IsDirty, "Cancelling Save As discarded the draft.");
            dialogs.Destination = Path.Combine(directory, "missing", "fail.dax");
            Invoke(form, "NewDocument");
            Assert(dialogs.Errors == 1 && document.IsDirty, "Failed save discarded the draft.");

            dialogs.Destination = Path.Combine(directory, "workbench.dax");
            Assert((bool)Invoke(form, "SaveDocument", false), "Workbench save failed.");
            Assert(!document.IsDirty && !form.Text.Contains(" *"), "Save did not clear dirty state.");
            editor.Text += "\n// preserve me";
            dialogs.Source = Path.Combine(directory, "missing.dax");
            dialogs.Answer = DialogResult.No;
            Invoke(form, "OpenDocument");
            Assert(editor.Text.EndsWith("// preserve me") && document.IsDirty, "Failed Open discarded edits.");

            dialogs.Source = dialogs.Destination;
            dialogs.Answer = DialogResult.Cancel;
            Invoke(form, "OpenDocument");
            Assert(document.IsDirty, "Cancelled Open discarded edits.");
            dialogs.Answer = DialogResult.No;
            Invoke(form, "OpenDocument");
            Assert(!document.IsDirty && editor.Text == File.ReadAllText(dialogs.Source), "Open did not load the selected file.");
            var history = Field<DaxQueryHistory>(form, "_history");
            history.Add("EVALUATE ROW(\"History\", 2)", true, 1);
            Invoke(form, "RefreshHistory");
            var list = Field<ListView>(form, "_historyList");
            var handle = list.Handle;
            list.Items[0].Selected = true;
            Invoke(form, "LoadSelectedHistory");
            Assert(document.FilePath == null && document.IsDirty && editor.Text.Contains("History"),
                "History must open an unsaved draft without targeting the previously opened file.");
            dialogs.Answer = DialogResult.No;
            Invoke(form, "NewDocument");
            Assert(document.FilePath == null && !document.IsDirty && editor.Text == "", "New did not reset document state.");
        }
    }

    private static T Field<T>(Form form, string name) => (T)form.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    private static object Invoke(Form form, string name, params object[] args) => form.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void ExpectFailure(Action action)
    {
        try { action(); }
        catch (IOException) { return; }
        catch (DecoderFallbackException) { return; }
        throw new InvalidOperationException("File operation should have failed.");
    }
    private sealed class Dialogs : IDaxDocumentDialogs
    {
        public string Source;
        public string Destination;
        public DialogResult Answer = DialogResult.Cancel;
        public int Errors;
        public string OpenPath(IWin32Window owner) => Source;
        public string SavePath(IWin32Window owner, string path) => Destination;
        public DialogResult SaveChanges(IWin32Window owner, string name) => Answer;
        public void Error(IWin32Window owner, string message) { Errors++; }
    }
}
