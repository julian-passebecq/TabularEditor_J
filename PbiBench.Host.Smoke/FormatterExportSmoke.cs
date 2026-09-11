using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PbiBench.Core.Dax;
using TabularEditor;
using TabularEditor.Dax;
using TabularEditor.PbiBench.Dax;
using TabularEditor.Scripting;
using TabularEditor.TOMWrapper;
using TabularEditor.UIServices;
using static QueryLifecycleSmoke;

internal static class FormatterExportSmoke
{
    internal static void Run()
    {
        var proxy = DaxFormatterProxy.Instance;
        var original = proxy.Transport;
        var transport = new RecordingTransport();
        proxy.Transport = transport;
        try
        {
            Assert(!RemoteFormatterConsent.Enabled, "Fresh process must deny remote formatting.");
            Denied(() => proxy.FormatDax("synthetic", false, false, false));
            Denied(() => proxy.FormatDaxMulti(new List<string> { "synthetic" }, false, false, false));
#pragma warning disable CS0612
            Denied(() => ScriptHelper.FormatDax("synthetic"));
#pragma warning restore CS0612
            using (var handler = new TabularModelHandler())
            {
                var measure = handler.Model.AddTable("Synthetic").AddMeasure("Synthetic", "1");
                Denied(() => ScriptHelper.FormatDax(measure));
                Denied(() => ScriptHelper.FormatDax(new[] { measure }));
            }
            Assert(transport.Primes == 0 && transport.Posts == 0, "Denied formatter touched transport/redirect.");
            RemoteFormatterConsent.SetEnabled(true);
            var oldTelemetry = Preferences.Current.CollectTelemetry;
            Preferences.Current.CollectTelemetry = true;
            try
            {
                proxy.FormatDax("EVALUATE ROW(\"x\",1)", true, true, true);
                ValidatePayload(transport.Body, false);
                proxy.FormatDaxMulti(new List<string> { "1", "2" }, false, false, false);
                ValidatePayload(transport.Body, true);
            }
            finally { Preferences.Current.CollectTelemetry = oldTelemetry; }
            // Administrative disable overrides consent at the actual shared boundary.
            var policy = typeof(Policies).GetProperty("DisableWebDaxFormatter");
            var admin = (bool)policy.GetValue(Policies.Instance);
            try
            {
                policy.SetValue(Policies.Instance, true);
                var calls = transport.Primes;
                Denied(() => proxy.FormatDax("synthetic", false, false, false));
                Assert(transport.Primes == calls, "Administrative denial touched transport.");
            }
            finally { policy.SetValue(Policies.Instance, admin); }
            RemoteFormatterConsent.SetEnabled(false);
            Denied(() => proxy.FormatDax("synthetic", false, false, false));
            RemoteFormatterConsent.SetEnabled(true);
            var posts = transport.Posts;
            transport.OnPrime = () => RemoteFormatterConsent.SetEnabled(false);
            Denied(() => proxy.FormatDax("synthetic", false, false, false));
            Assert(transport.Posts == posts, "Revocation during redirect allowed payload send.");
            transport.OnPrime = null;
            RemoteFormatterConsent.SetEnabled(true);
            UdfAndBatch(transport);
            FormattingUi(transport);
            ExpressionUi(transport);
            CliDenial();
            Export();
        }
        finally { proxy.Transport = original; RemoteFormatterConsent.SetEnabled(false); }
        Console.WriteLine("T08/T09: formatter denial/payload/script/UDF/stale/undo and atomic CSV/UI failures PASS (recording transport, no network)");
    }
    private static void ValidatePayload(string body, bool multi)
    {
        var obj = JObject.Parse(body);
        var allowed = new[] { "Dax", "MaxLineLenght", "SkipSpaceAfterFunctionName", "ListSeparator", "DecimalSeparator", "CallerApp", "CallerVersion" };
        Assert(obj.Properties().Select(p => p.Name).OrderBy(x => x).SequenceEqual(allowed.OrderBy(x => x)), "Unexpected serialized formatter field/telemetry.");
        Assert(multi ? obj["Dax"] is JArray : obj["Dax"].Type == JTokenType.String, "Single/multi payload shape.");
        Assert((string)obj["ListSeparator"] == (multi ? "," : ";") && (int)obj["MaxLineLenght"] == (multi ? 0 : 1), "Formatting options changed.");
    }
    private static void UdfAndBatch(RecordingTransport transport)
    {
        using (var handler = new TabularModelHandler(compatibilityLevel: 1702))
        {
            var udf = handler.Model.AddFunction("synthetic");
            foreach (var comment in new[] { "", "// one\n", "/* multi\nline */\n" })
            {
                udf.Expression = comment + "() => 1";
                transport.Response = body => JsonConvert.SerializeObject(((JArray)JObject.Parse(body)["Dax"]).Select(dax => new { formatted = (string)dax, errors = new object[0] }));
                ScriptHelper.BeforeScriptExecution(); udf.FormatDax(); ScriptHelper.AfterScriptExecution();
                Assert(udf.Expression.Replace("\r", "") == (comment + "() => 1"), "UDF wrapper/comment extraction changed.");
            }
            var table = handler.Model.AddTable("Synthetic");
            var measures = new[] { table.AddMeasure("a", "1"), table.AddMeasure("b", "2") };
            transport.Response = body => JsonConvert.SerializeObject(((JArray)JObject.Parse(body)["Dax"]).Select(dax => new { formatted = "x :=  " + ((string)dax).Substring(4), errors = new object[0] }));
            ScriptHelper.FormatDax(measures);
            Assert(measures[0].Expression == "1" && measures[1].Expression == "2", "Actual batch caller changed expressions.");
        }
        transport.Response = null;
    }
    private static void FormattingUi(RecordingTransport transport)
    {
        using (var form = new PbiBenchDaxWorkbenchForm(null, "", "offline"))
        {
            var handle = form.Handle;
            var editor = Field<RichTextBox>(form, "_queryEditor");
            editor.Text = "EVALUATE ROW(\"x\",1)";
            var before = editor.Text;
            transport.Response = body => JsonConvert.SerializeObject(new { formatted = "EVALUATE\nROW(\"x\", 1)", errors = new object[0] });
            Invoke(form, "FormatDraft", false); Wait(() => !Field<bool>(form, "_formatting"));
            Assert(editor.Text != before && editor.CanUndo, "Successful format not undoable.");
            editor.Undo(); Assert(editor.Text == before, "One undo did not restore draft.");
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                transport.OnPost = () => { entered.Set(); Assert(release.Wait(10000), "Format not released."); };
                Invoke(form, "FormatDraft", false); Wait(() => entered.IsSet);
                editor.Text = "edited while formatting";
                release.Set(); Wait(() => !Field<bool>(form, "_formatting"));
                Assert(editor.Text == "edited while formatting", "Stale formatter overwrote draft.");
            }
            transport.OnPost = null;
            foreach (var response in new[] { "invalid json", "{\"formatted\":null}", "{\"formatted\":\"bad\",\"errors\":[{}]}" })
            {
                transport.Response = body => response;
                Invoke(form, "FormatDraft", false); Wait(() => !Field<bool>(form, "_formatting"));
                Assert(editor.Text == "edited while formatting", "Failed formatter changed draft.");
            }
            transport.OnPost = () => throw new TimeoutException("synthetic private sentinel");
            Invoke(form, "FormatDraft", false); Wait(() => !Field<bool>(form, "_formatting"));
            Assert(!Field<Label>(form, "_status").Text.Contains("sentinel") && editor.Text == "edited while formatting", "Formatter timeout leaked/changed draft.");
            transport.OnPost = null;
            foreach (var cancel in new[] { "cancel", "close", "revoke" })
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                transport.Response = body => "{\"formatted\":\"replacement\",\"errors\":[]}";
                transport.OnPost = () => { entered.Set(); Assert(release.Wait(10000), "Pending format not released."); };
                Invoke(form, "FormatDraft", false); Wait(() => entered.IsSet);
                if (cancel == "cancel") Invoke(form, "CancelFormatting");
                if (cancel == "close")
                {
                    // A pristine document avoids a real Save prompt, exercising the actual close path.
                    Field<DaxDocument>(form, "_document").New(editor.Text);
                    Invoke(form, "OnWorkbenchClosing", form, new FormClosingEventArgs(CloseReason.UserClosing, false));
                }
                if (cancel == "revoke") RemoteFormatterConsent.SetEnabled(false);
                release.Set(); Wait(() => !Field<bool>(form, "_formatting"));
                Assert(editor.Text == "edited while formatting", "Cancelled/closed/revoked formatter replaced draft.");
                typeof(PbiBenchDaxWorkbenchForm).GetField("_closing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, false);
                RemoteFormatterConsent.SetEnabled(true);
            }
            transport.OnPost = null;
        }
    }
    private static void ExpressionUi(RecordingTransport transport)
    {
        var oldSingleton = FormMain.Singleton;
        using (var form = new FormMain())
        {
            var handle = form.Handle;
            var editor = Field<Control>(form, "txtExpression");
            editor.Text = "1";
            RemoteFormatterConsent.SetEnabled(false);
            var calls = transport.Primes;
            Invoke(form, "actExpressionFormatDAX_Execute", null, EventArgs.Empty);
            Wait(() => !Field<bool>(form, "_pbiBenchExpressionFormatting"));
            Assert(transport.Primes == calls && Field<ToolStripStatusLabel>(form, "lblStatus").Text.Contains("Enable session consent"), "Actual expression denial bypassed gate or lacked guidance.");
            RemoteFormatterConsent.SetEnabled(true);
            transport.Response = body => "{\"formatted\":\"x :=  2\",\"errors\":[]}";
            Invoke(form, "actExpressionFormatDAX_Execute", null, EventArgs.Empty);
            Wait(() => !Field<bool>(form, "_pbiBenchExpressionFormatting"));
            Assert(editor.Text == "2", "Actual expression formatter did not apply response.");
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            {
                transport.OnPost = () => { entered.Set(); Assert(release.Wait(10000), "Expression format not released."); };
                Invoke(form, "actExpressionFormatDAX_Execute", null, EventArgs.Empty); Wait(() => entered.IsSet);
                editor.Text = "3"; editor.Text = "2";
                transport.Response = body => "{\"formatted\":\"x :=  4\",\"errors\":[]}";
                release.Set(); Wait(() => !Field<bool>(form, "_pbiBenchExpressionFormatting"));
                Assert(editor.Text == "2", "Expression edit/undo revision was ignored.");
            }
            transport.OnPost = null;
        }
        FormMain.Singleton = oldSingleton;
    }
    private static void CliDenial()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pbibench-cli-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var model = Path.Combine(folder, "synthetic.bim");
            var script = Path.Combine(folder, "format.cs");
            using (var handler = new TabularModelHandler())
            {
                handler.Model.AddTable("Synthetic").AddMeasure("x", "1");
                handler.Save(model, SaveFormat.ModelSchemaOnly, TabularEditor.TOMWrapper.Serialization.SerializeOptions.Default);
            }
            foreach (var code in new[] { "FormatDax(\"1\");", "Model.AllMeasures.FormatDax();" })
            {
                File.WriteAllText(script, code);
                var start = new System.Diagnostics.ProcessStartInfo(typeof(FormMain).Assembly.Location,
                    "\"" + model + "\" -S \"" + script + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    try { Wait(() => process.HasExited); }
                    catch { if (!process.HasExited) process.Kill(); throw; }
                    var output = stdout.Result + stderr.Result;
                    Assert(process.ExitCode != 0 && output.Contains("Remote DAX formatting is disabled"),
                        "Fresh CLI process did not clearly deny formatter: " + output);
                }
            }
        }
        finally { Directory.Delete(folder, true); }
    }
    private static void Export()
    {
        var result = new DaxQueryResult { Columns = new[] { "text", "number", "date", "null" }, Rows = new[] {
            new object[] { "=1,\"quoted\"\r\n界", 1234.5m, new DateTime(2026, 9, 8, 12, 30, 0, DateTimeKind.Utc), null } } };
        var folder = Path.Combine(Path.GetTempPath(), "pbibench-export-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "result.csv");
        var culture = Thread.CurrentThread.CurrentCulture;
        try
        {
            foreach (var name in new[] { "en-US", "fr-CH" })
            {
                Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(name);
                DaxCsvExport.Save(path, result);
                var bytes = File.ReadAllBytes(path);
                Assert(bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }), "CSV missing UTF-8 BOM.");
                var fields = DecodeCsv(File.ReadAllText(path));
                Assert(fields.Count == 8 && fields[4] == (string)result.Rows[0][0] && fields[5] == "1234.5" && fields[6] == "2026-09-08T12:30:00.0000000Z" && fields[7] == "", "Decoded CSV data differs across culture.");
            }
            var original = File.ReadAllBytes(path);
            try { DaxCsvExport.Save(path, result, (writer, rows) => { writer.Write("partial"); throw new IOException("synthetic"); }); throw new Exception("Injected write failure did not propagate."); }
            catch (IOException) { }
            Assert(File.ReadAllBytes(path).SequenceEqual(original) && Directory.GetFiles(folder).Length == 1, "Partial export damaged destination/temp cleanup.");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { DaxCsvExport.Save(path, result); throw new Exception("Locked destination was replaced."); }
                catch (IOException) { }
            }
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try { DaxCsvExport.Save(path, result); throw new Exception("Read-only destination replaced."); }
            catch (UnauthorizedAccessException) { }
            finally { File.SetAttributes(path, FileAttributes.Normal); }
            using (var form = new PbiBenchDaxWorkbenchForm(null, "", "offline"))
            {
                var handle = form.Handle;
                typeof(PbiBenchDaxWorkbenchForm).GetField("_currentResult", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, result);
                Invoke(form, "BindResult", result);
                var draft = Field<RichTextBox>(form, "_queryEditor").Text;
                var called = false;
                form.ExportPath = owner => null;
                form.SaveCsv = (p, r) => called = true;
                Invoke(form, "ExportCsv"); Assert(!called, "Cancelled dialog wrote a file.");
                form.ExportPath = owner => path;
                form.SaveCsv = (p, r) => { throw new IOException("sensitive path sentinel"); };
                Invoke(form, "ExportCsv");
                Assert(Field<Label>(form, "_status").Text.StartsWith("CSV export failed") && !Field<Label>(form, "_status").Text.Contains("sentinel"), "Actual UI export error not safe/handled.");
                Assert(Field<RichTextBox>(form, "_queryEditor").Text == draft && Field<DaxQueryResult>(form, "_currentResult") == result && Field<Button>(form, "_exportButton").Enabled, "Export failure lost draft/results.");
            }
            Assert(File.ReadAllBytes(path).SequenceEqual(original), "Failed replacement damaged destination.");
        }
        finally { Thread.CurrentThread.CurrentCulture = culture; Directory.Delete(folder, true); }
    }
    private static List<string> DecodeCsv(string text)
    {
        var fields = new List<string>(); var value = new StringBuilder(); var quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { value.Append('"'); i++; } else quoted = !quoted; }
            else if (!quoted && (c == ',' || c == '\r')) { fields.Add(value.ToString()); value.Clear(); if (c == '\r') i++; }
            else value.Append(c);
        }
        Assert(!quoted && value.Length == 0, "Malformed CSV."); return fields;
    }
    private static void Denied(Action action)
    {
        try { action(); throw new Exception("Expected explicit formatter denial."); }
        catch (InvalidOperationException ex) { Assert(ex.Message.Contains("disabled"), "Unclear denial."); }
    }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    private static void Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);
    private sealed class RecordingTransport : IDaxFormatterTransport
    {
        internal int Primes, Posts;
        internal string Body;
        internal Action OnPrime, OnPost;
        internal Func<string, string> Response;
        public string Prime(string uri, int timeout) { Primes++; OnPrime?.Invoke(); return uri; }
        public string Post(string uri, string body, int timeout)
        {
            Posts++; Body = body; OnPost?.Invoke();
            return Response != null ? Response(body) : uri.EndsWith("multi") ? "[]" : "{\"formatted\":\"synthetic\",\"errors\":[]}";
        }
    }
}

