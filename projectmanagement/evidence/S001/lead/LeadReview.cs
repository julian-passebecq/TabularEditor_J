using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using TabularEditor.Dax;
using TabularEditor.PbiBench.Dax;

internal static class LeadReview
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static T Field<T>(object o, string n) => (T)o.GetType().GetField(n, Private).GetValue(o);
    static void Invoke(object o, string n, params object[] a) => o.GetType().GetMethod(n, Private).Invoke(o, a);
    static void Wait(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!done()) { Application.DoEvents(); if(DateTime.UtcNow > until) throw new Exception("Probe watchdog"); Thread.Sleep(1); }
    }
    [STAThread] static int Main()
    {
        int failures = 0;
        foreach (var culture in new[] { "en-US", "fr-CH" })
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var d = 1.0000000000000002d;
            var f = 1.00000012f;
            foreach(var v in new object[] { d, f })
            {
                var s = DaxResultRetention.FormatValue(v);
                bool equal = v is double dv ? double.Parse(s, CultureInfo.InvariantCulture).Equals(dv)
                    : float.Parse(s, CultureInfo.InvariantCulture).Equals((float)v);
                var expected = ((IFormattable)v).ToString("R", CultureInfo.InvariantCulture);
                Console.WriteLine("NUMERIC " + culture + " " + v.GetType().Name + " expected=" + expected + " actual=" + s + " exact=" + equal);
                if(!equal) failures++;
            }
            using(var writer = new StringWriter(CultureInfo.InvariantCulture))
            {
                DaxCsvExport.Write(writer, new DaxQueryResult { Columns=new[]{"double","single"}, Rows=new[]{new object[]{d,f}} });
                Console.WriteLine("ACTUAL_CSV " + writer.ToString().Replace("\r", "\\r").Replace("\n", "\\n"));
            }
        }
        var root = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        var first = Path.Combine(root, "same-a.dax"); var second = Path.Combine(root, "same-b.dax");
        File.WriteAllText(first, "EVALUATE ROW(\"x\",1)"); File.WriteAllText(second, "EVALUATE ROW(\"x\",1)");
        var dialogs = new Dialogs { Path = first };
        var proxy = DaxFormatterProxy.Instance; var old = proxy.Transport;
        using(var entered=new ManualResetEventSlim())
        using(var release=new ManualResetEventSlim())
        using(var form=new PbiBenchDaxWorkbenchForm(null,"","offline", dialogs))
        {
            var handle=form.Handle;
            try
            {
                proxy.Transport = new Transport(entered,release); RemoteFormatterConsent.SetEnabled(true);
                Invoke(form,"OpenDocument");
                var editor=Field<RichTextBox>(form,"_queryEditor"); var before=editor.Text;
                Invoke(form,"FormatDraft",false); Wait(()=>entered.IsSet);
                var revision=Field<long>(form,"_textRevision");
                dialogs.Path=second; Invoke(form,"OpenDocument");
                Console.WriteLine("IDENTITY_SWITCH file="+Path.GetFileName(Field<DaxDocument>(form,"_document").FilePath)+" revisionBefore="+revision+" revisionAfter="+Field<long>(form,"_textRevision"));
                release.Set(); Wait(()=>!Field<bool>(form,"_formatting"));
                bool unchanged=editor.Text==before;
                Console.WriteLine("STALE_FORMAT identical-text different-file preserved="+unchanged+" dirty="+Field<DaxDocument>(form,"_document").IsDirty);
                if(!unchanged) failures++;
            }
            finally { release.Set(); proxy.Transport=old; RemoteFormatterConsent.SetEnabled(false); }
        }
        Console.WriteLine("LEAD_PROBE detected violations="+failures);
        return failures==0?0:1;
    }
    sealed class Dialogs : IDaxDocumentDialogs
    {
        internal string Path;
        public string OpenPath(IWin32Window owner)=>Path;
        public string SavePath(IWin32Window owner,string currentPath)=>null;
        public DialogResult SaveChanges(IWin32Window owner,string name)=>DialogResult.Cancel;
        public void Error(IWin32Window owner,string message)=>throw new Exception(message);
    }
    sealed class Transport : IDaxFormatterTransport
    {
        readonly ManualResetEventSlim entered, release;
        internal Transport(ManualResetEventSlim a,ManualResetEventSlim b){entered=a;release=b;}
        public string Prime(string uri,int timeout)=>uri;
        public string Post(string uri,string body,int timeout)
        {
            entered.Set(); if(!release.Wait(10000)) throw new Exception("Probe release watchdog");
            return "{\"formatted\":\"EVALUATE\\nROW(\\\"x\\\", 1)\",\"errors\":[]}";
        }
    }
}
