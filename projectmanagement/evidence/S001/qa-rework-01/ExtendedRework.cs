using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using TabularEditor;
using TabularEditor.Dax;
using TabularEditor.PbiBench.Dax;
using TabularEditor.TOMWrapper;
using TabularEditor.TOMWrapper.Utils;
using static QueryLifecycleSmoke;

internal static class Rework01Smoke
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, Flags).GetValue(target);
    private static object Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags).Invoke(target, args);
    private static void Handles(Control control) { var handle = control.Handle; foreach (Control child in control.Controls) Handles(child); }
    internal static void Run()
    {
        Numbers();
        var prior = DaxFormatterProxy.Instance.Transport;
        var consent = RemoteFormatterConsent.Enabled;
        var folder = Path.Combine(Path.GetTempPath(), "pbibench-rework-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        RemoteFormatterConsent.SetEnabled(true);
        try { Documents(folder); Expressions(); }
        finally { DaxFormatterProxy.Instance.Transport = prior; RemoteFormatterConsent.SetEnabled(consent); Directory.Delete(folder, true); }
        Console.WriteLine("T14/T15 developer: numeric CSV/grid fidelity, document identity and expression context PASS");
    }
    private static void Numbers()
    {
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            foreach (var locale in new[] { "en-US", "fr-CH" })
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(locale);
                object[] values = { 1.0000000000000002d, 1.00000012f, -1.0000000000000002d,
                    double.Epsilon, float.Epsilon, double.MaxValue, float.MaxValue, 0d, BitConverter.Int64BitsToDouble(long.MinValue),
                    double.NaN, double.PositiveInfinity, double.NegativeInfinity, float.NaN, float.PositiveInfinity,
                    BitConverter.ToSingle(BitConverter.GetBytes(int.MinValue), 0) };
                var result = new DaxQueryResult { Columns = new[] { "value" }, Rows = values.Select(v => new[] { v }).ToArray() };
                using (var writer = new StringWriter())
                using (var form = new PbiBenchDaxWorkbenchForm(null, "", "offline"))
                {
                    DaxCsvExport.Write(writer, result);
                    var fields = writer.ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(s => s.Substring(1, s.Length - 2)).ToArray();
                    Handles(form);
                    form.GetType().GetField("_currentResult", Flags).SetValue(form, result);
                    Invoke(form, "BindResult", result);
                    for (var i = 0; i < values.Length; i++)
                    {
                        if (values[i] is double d) Assert(double.Parse(fields[i], CultureInfo.InvariantCulture).Equals(d), "Double CSV did not round-trip.");
                        else Assert(float.Parse(fields[i], CultureInfo.InvariantCulture).Equals((float)values[i]), "Single CSV did not round-trip.");
                        var cell = new DataGridViewCellValueEventArgs(0, i);
                        Invoke(form, "Results_CellValueNeeded", form, cell);
                        Assert((string)cell.Value == fields[i], "Grid differs from CSV representation.");
                    }
                    Assert(fields[0] == "1.0000000000000002" && fields[1] == "1.00000012", "Reported numeric regression remains.");
                    Assert(fields[7] == "0" && fields[8] == "0" && fields[14] == "0", "Zero policy changed.");
                    Assert(fields[9] == "NaN" && fields[10] == "Infinity" && fields[11] == "-Infinity", "Non-finite policy changed.");
                }
            }
        }
        finally { Thread.CurrentThread.CurrentCulture = culture; }
    }
    private static void Documents(string folder)
    {
        var a = Path.Combine(folder, "a.dax"); var b = Path.Combine(folder, "b.dax");
        File.WriteAllText(a, "1"); File.WriteAllText(b, "1");
        foreach (var action in new[] { "open", "reopen", "new", "history", "failed", "cancelled", "save", "saveas", "cancelnew", "cancelopen", "cancelsave", "failsave" })
        using (var transport = new HeldTransport())
        {
            DaxFormatterProxy.Instance.Transport = transport;
            var dialogs = new Dialogs { Open = a, Save = Path.Combine(folder, "saved.dax") };
            using (var form = new PbiBenchDaxWorkbenchForm(null, "", "offline", dialogs))
            {
                Handles(form);
                Invoke(form, "OpenDocument");
                var editor = Field<RichTextBox>(form, "_queryEditor");
                var document = Field<DaxDocument>(form, "_document");
                if (action == "new" || action == "cancelsave" || action == "failsave") { Invoke(form, "NewDocument"); }
                if (action.StartsWith("cancel") || action == "failsave") { editor.Text = "dirty draft"; dialogs.Answer = DialogResult.Cancel; }
                var generation = Field<long>(form, "_documentGeneration");
                Invoke(form, "FormatDraft", false); Wait(() => transport.Entered.IsSet);
                switch (action)
                {
                    case "open": dialogs.Open = b; Invoke(form, "OpenDocument"); break;
                    case "reopen": Invoke(form, "OpenDocument"); break;
                    case "new": Invoke(form, "NewDocument"); break;
                    case "history":
                        Field<DaxQueryHistory>(form, "_history").Add("1", new DaxQueryResult());
                        Invoke(form, "RefreshHistory");
                        Field<ListView>(form, "_historyList").Items[0].Selected = true;
                        Invoke(form, "LoadSelectedHistory"); break;
                    case "failed": dialogs.Open = Path.Combine(folder, "missing.dax"); Invoke(form, "OpenDocument"); break;
                    case "cancelled": dialogs.Open = null; Invoke(form, "OpenDocument"); break;
                    case "cancelnew": Invoke(form, "NewDocument"); break;
                    case "cancelopen": dialogs.Open = b; Invoke(form, "OpenDocument"); break;
                    case "cancelsave": dialogs.Answer = DialogResult.Yes; dialogs.Save = null; Invoke(form, "NewDocument"); break;
                    case "failsave": dialogs.Answer = DialogResult.Yes; dialogs.Save = Path.Combine(folder, "missing", "save.dax"); Invoke(form, "NewDocument"); break;
                    case "save": Invoke(form, "SaveDocument", false); break;
                    case "saveas": Invoke(form, "SaveDocument", true); break;
                }
                var replacement = new[] { "open", "reopen", "new", "history" }.Contains(action);
                Assert(Field<long>(form, "_documentGeneration") == generation + (replacement ? 1 : 0), "Generation changed incorrectly: " + action);
                var text = editor.Text; var path = document.FilePath; var dirty = document.IsDirty; var undo = editor.CanUndo;
                transport.Release.Set(); Wait(() => !Field<bool>(form, "_formatting"));
                if (replacement)
                    Assert(editor.Text == text && document.FilePath == path && document.IsDirty == dirty && editor.CanUndo == undo, "Stale format modified replacement: " + action);
                else
                {
                    Assert(editor.Text == "2" && document.IsDirty && editor.CanUndo && document.FilePath == path, "Same-buffer format was incorrectly discarded: " + action);
                    editor.Undo(); Assert(editor.Text == text, "Same-buffer format lost undo.");
                }
            }
        }
    }
    private static void Expressions()
    {
        var singleton = FormMain.Singleton;
        try
        {
            foreach (var action in new[] { "same", "object", "property", "handler", "objectone", "propertyone", "handlerone" })
            using (var otherHandler = new TabularModelHandler())
            using (var handler = new TabularModelHandler(compatibilityLevel: 1601))
            using (var form = new FormMain())
            using (var transport = new HeldTransport { Response = "{\"formatted\":\"x :=  2\",\"errors\":[]}" })
            {
                DaxFormatterProxy.Instance.Transport = transport;
                Handles(form);
                var table = handler.Model.AddTable("Synthetic");
                var first = table.AddMeasure("First", "1"); var second = table.AddMeasure("Second", "1");
                first.FormatStringExpression = "1";
                var handlerProperty = form.UI.GetType().GetProperty("Handler", Flags);
                handlerProperty.SetValue(form.UI, handler);
                form.UI.LoadTabularModelToUI();
                Invoke(form.UI, "ExpressionEditor_Preview", first);
                var editor = Field<Control>(form, "txtExpression");
                var generation = form.UI.ExpressionEditorContextGeneration;
                Invoke(form, "actExpressionFormatDAX_Execute", null, EventArgs.Empty); Wait(() => transport.Entered.IsSet);
                if (action == "objectone") Invoke(form.UI, "ExpressionEditor_Preview", second);
                if (action == "propertyone") form.UI.GetType().GetProperty("CurrentDaxProperty", Flags).SetValue(form.UI, DAXProperty.FormatStringExpression);
                if (action == "handlerone") handlerProperty.SetValue(form.UI, otherHandler);
                if (action == "object") { Invoke(form.UI, "ExpressionEditor_Preview", second); Invoke(form.UI, "ExpressionEditor_Preview", first); }
                if (action == "property")
                {
                    var property = form.UI.GetType().GetProperty("CurrentDaxProperty", Flags);
                    property.SetValue(form.UI, DAXProperty.FormatStringExpression); property.SetValue(form.UI, DAXProperty.Expression);
                }
                if (action == "handler") { handlerProperty.SetValue(form.UI, otherHandler); handlerProperty.SetValue(form.UI, handler); }
                Assert(editor.Text == "1", "Context fixture must preserve identical content.");
                Assert(action == "same" ? generation == form.UI.ExpressionEditorContextGeneration : generation < form.UI.ExpressionEditorContextGeneration,
                    "Context transition was not observed: " + action);
                transport.Release.Set(); Wait(() => !Field<bool>(form, "_pbiBenchExpressionFormatting"));
                Assert(editor.Text == (action == "same" ? "2" : "1"), "Expression context guard failed: " + action);
            }
        }
        finally { FormMain.Singleton = singleton; }
    }
    private sealed class HeldTransport : IDaxFormatterTransport, IDisposable
    {
        internal readonly ManualResetEventSlim Entered = new ManualResetEventSlim();
        internal readonly ManualResetEventSlim Release = new ManualResetEventSlim();
        internal string Response = "{\"formatted\":\"2\",\"errors\":[]}";
        public string Prime(string uri, int timeout) => uri;
        public string Post(string uri, string body, int timeout) { Entered.Set(); if (!Release.Wait(10000)) throw new TimeoutException("Fixture watchdog"); return Response; }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }
    private sealed class Dialogs : IDaxDocumentDialogs
    {
        internal string Open, Save; internal DialogResult Answer = DialogResult.No;
        public string OpenPath(IWin32Window owner) => Open;
        public string SavePath(IWin32Window owner, string currentPath) => Save;
        public DialogResult SaveChanges(IWin32Window owner, string name) => Answer;
        public void Error(IWin32Window owner, string message) { }
    }
}


