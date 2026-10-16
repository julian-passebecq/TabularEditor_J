using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AnalysisServices;
using Tom = Microsoft.AnalysisServices.Tabular;

// Experiment: what does the engine do after each cancel method, seen through DMVs and msmdsrv CPU?
// args: server db pid methods(csv) cancelAtMs log
internal static class P
{
    const string Slow = "EVALUATE ROW(\"n\", COUNTROWS(FILTER(CROSSJOIN(SELECTCOLUMNS(GENERATESERIES(1,6000),\"a\",[Value]),SELECTCOLUMNS(GENERATESERIES(1,6000),\"b\",[Value])), MOD([a]*[b],7)=3)))";
    static string Query = Slow;
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static StreamWriter log;
    static void L(string s) { var line = (clock.ElapsedMilliseconds / 1000.0).ToString("0.000").PadLeft(8) + " " + s; Console.WriteLine(line); log.WriteLine(line); log.Flush(); }

    static int Main(string[] a)
    {
        string server = a[0], db = a[1]; int pid = int.Parse(a[2]); var methods = a[3].Split(','); int at = int.Parse(a[4]);
        log = new StreamWriter(a[5], false);
        if (a.Length > 6 && a[6] == "multi") Query = Slow + "\n" + Slow.Replace("=3", "=4") + "\n" + Slow.Replace("=3", "=5");
        if (a.Length > 6 && a[6] == "sumx") Query = "EVALUATE ROW(\"n\", SUMX(GENERATESERIES(1,3000), COUNTROWS(FILTER(GENERATESERIES(1,12000), MOD([Value],7)=3))))";
        if (a.Length > 6 && a[6] == "se") Query = "EVALUATE ROW(\"n\", SUMX(SELECTCOLUMNS(GENERATESERIES(1," + (a.Length > 7 ? a[7] : "2000") + "),\"k\",[Value]), VAR k = [k] RETURN CALCULATE(COUNTROWS(Big), Big[V] > k * 1000, Big[G] <> MOD(k, 977))))";
        L("query: " + Query);
        if (db != "tmp_pbibench-cancel_20261016") { L("refusing " + db); return 2; }
        var admin = new Tom.Server(); admin.Connect("Data Source=" + server);
        L("server " + admin.Version + " mode=" + admin.ServerMode + " dbs=" + admin.Databases.Count);
        if (admin.Databases.ContainsName(db)) { L("exists, refusing"); return 3; }
        try
        {
            Create(admin, db); L("created " + db);
            foreach (var m in methods) Run(server, db, pid, m, at);
        }
        catch (Exception e) { L("ERROR " + e); }
        finally
        {
            admin.Refresh(true);
            if (admin.Databases.ContainsName(db)) admin.Databases.GetByName(db).Drop();
            admin.Refresh(true); L("dropped exists=" + admin.Databases.ContainsName(db) + " dbs=" + admin.Databases.Count);
            admin.Disconnect();
        }
        return 0;
    }

    static void Run(string server, string db, int pid, string method, int at)
    {
        L("=== method " + method);
        var q = new Microsoft.AnalysisServices.Tabular.Server(); q.Connect("Data Source=" + server + ";Initial Catalog=" + db);
        var sid = q.SessionID; L("query session " + sid);
        var stop = false; var proc = Process.GetProcessById(pid);
        var sampler = new Thread(() => Sample(server, sid, proc, () => stop)) { IsBackground = true };
        sampler.Start(); Thread.Sleep(800);
        long t0 = clock.ElapsedMilliseconds; long ret = -1; string outcome = "";
        var exec = new Thread(() =>
        {
            try
            {
                XmlaResultCollection res;
                using (var r = q.ExecuteReader("<Statement>" + System.Security.SecurityElement.Escape(Query) + "</Statement>", out res,
                    new Dictionary<string, string> { { "Catalog", db }, { "Format", "Tabular" } }))
                { while (r != null && r.Read()) outcome += " row:" + r.GetValue(0); }
                foreach (XmlaResult x in res) foreach (XmlaMessage m in x.Messages) outcome += " msg:" + m.Description;
                outcome = "completed" + outcome;
            }
            catch (Exception e) { outcome = "exception " + e.GetType().Name; }
            ret = clock.ElapsedMilliseconds;
        }) { IsBackground = true };
        exec.Start();
        if (method != "none")
        {
            Thread.Sleep(at);
            L("cancel " + method + " at +" + (clock.ElapsedMilliseconds - t0));
            using (var c = new Microsoft.AnalysisServices.Tabular.Server())
            {
                c.Connect("Data Source=" + server);
                string spid = Spid(c, sid);
                L("spid of session = " + spid);
                string body = method == "session-assoc" ? "<SessionID>" + sid + "</SessionID><CancelAssociated>true</CancelAssociated>" :
                              method == "spid" ? "<SPID>" + spid + "</SPID>" :
                              method == "spid-assoc" ? "<SPID>" + spid + "</SPID><CancelAssociated>true</CancelAssociated>" :
                              "<SessionID>" + sid + "</SessionID>";
                var rr = c.Execute("<Cancel xmlns=\"http://schemas.microsoft.com/analysisservices/2003/engine\">" + body + "</Cancel>");
                L("cancel sent at +" + (clock.ElapsedMilliseconds - t0) + " messages=" + string.Join("|", rr.Cast<XmlaResult>().SelectMany(x => x.Messages.Cast<XmlaMessage>()).Select(x => x.Description)));
                c.Disconnect();
            }
        }
        exec.Join(120000);
        L("client returned at +" + (ret - t0) + " outcome=" + outcome);
        Thread.Sleep(28000);
        stop = true; sampler.Join(3000);
        try { q.Disconnect(); } catch { }
        Thread.Sleep(2000);
    }

    static string Spid(Microsoft.AnalysisServices.Tabular.Server c, string sid)
    {
        foreach (var row in Rows(c, "SELECT SESSION_ID, SESSION_SPID FROM $SYSTEM.DISCOVER_SESSIONS"))
            if (string.Equals(Convert.ToString(row["SESSION_ID"]), sid, StringComparison.OrdinalIgnoreCase)) return Convert.ToString(row["SESSION_SPID"]);
        return "?";
    }

    static List<Dictionary<string, object>> Rows(Microsoft.AnalysisServices.Tabular.Server c, string dmv)
    {
        var list = new List<Dictionary<string, object>>();
        XmlaResultCollection res;
        using (var r = c.ExecuteReader("<Statement>" + System.Security.SecurityElement.Escape(dmv) + "</Statement>", out res, new Dictionary<string, string> { { "Format", "Tabular" } }))
            while (r != null && r.Read())
            {
                var d = new Dictionary<string, object>();
                for (int i = 0; i < r.FieldCount; i++) d[r.GetName(i)] = r.GetValue(i);
                list.Add(d);
            }
        return list;
    }

    static PerformanceCounter CpuCounter(int pid)
    {
        try
        {
            foreach (var inst in new PerformanceCounterCategory("Process").GetInstanceNames().Where(n => n.StartsWith("msmdsrv")))
                using (var idc = new PerformanceCounter("Process", "ID Process", inst, true))
                    if ((int)idc.RawValue == pid) return new PerformanceCounter("Process", "% Processor Time", inst, true);
        }
        catch (Exception e) { L("no cpu counter: " + e.Message); }
        return null;
    }

    static void Sample(string server, string sid, Process proc, Func<bool> stop)
    { try { SampleCore(server, sid, proc, stop); } catch (Exception e) { L("sampler error " + e.Message); } }

    static void SampleCore(string server, string sid, Process proc, Func<bool> stop)
    {
        using (var s = new Microsoft.AnalysisServices.Tabular.Server())
        {
            s.Connect("Data Source=" + server);
            var counter = CpuCounter(proc.Id); try { counter?.NextValue(); } catch { counter = null; }
            while (!stop())
            {
                string sess = "gone";
                foreach (var row in Rows(s, "SELECT SESSION_ID, SESSION_SPID, SESSION_STATUS, SESSION_CPU_TIME_MS FROM $SYSTEM.DISCOVER_SESSIONS"))
                    if (string.Equals(Convert.ToString(row["SESSION_ID"]), sid, StringComparison.OrdinalIgnoreCase))
                        sess = "spid=" + row["SESSION_SPID"] + " status=" + row["SESSION_STATUS"] + " cpu=" + row["SESSION_CPU_TIME_MS"];
                string cmd = "none";
                foreach (var row in Rows(s, "SELECT SESSION_SPID, COMMAND_ELAPSED_TIME_MS, COMMAND_CPU_TIME_MS, COMMAND_END_TIME, COMMAND_TEXT FROM $SYSTEM.DISCOVER_COMMANDS"))
                    if (Convert.ToString(row["COMMAND_TEXT"]).Contains("GENERATESERIES(1,") && !Convert.ToString(row["COMMAND_TEXT"]).Contains("DISCOVER"))
                        cmd = "spid=" + row["SESSION_SPID"] + " el=" + row["COMMAND_ELAPSED_TIME_MS"] + " cpu=" + row["COMMAND_CPU_TIME_MS"] + " end=" + (row["COMMAND_END_TIME"] is DateTime dt ? dt.ToString("HH:mm:ss.fff") : "-");
                string pct = "n/a"; try { if (counter != null) pct = (counter.NextValue() / Environment.ProcessorCount).ToString("0") + "%"; } catch { }
                var line = "session[" + sess + "] command[" + cmd + "] msmdsrvCpu=" + pct;
                L("  " + line);
                Thread.Sleep(200);
            }
        }
    }

    static void Create(Tom.Server admin, string dbName)
    {
        var d = new Tom.Database { Name = dbName, ID = dbName, CompatibilityLevel = 1600, StorageEngineUsed = StorageEngineUsed.TabularMetadata };
        d.Model = new Tom.Model { Name = "Model" };
        var sales = new Tom.Table { Name = "Sales" };
        sales.Partitions.Add(new Tom.Partition { Name = "Sales", Source = new Tom.CalculatedPartitionSource { Expression = "DATATABLE(\"Region\", STRING, \"Amount\", INTEGER, {{\"North\", 10}, {\"South\", 20}, {\"East\", 5}})" } });
        d.Model.Tables.Add(sales);
        var big = new Tom.Table { Name = "Big" };
        big.Partitions.Add(new Tom.Partition { Name = "Big", Source = new Tom.CalculatedPartitionSource { Expression = "SELECTCOLUMNS(GENERATESERIES(1, 3000000), \"V\", [Value], \"G\", MOD([Value], 977))" } });
        d.Model.Tables.Add(big);
        admin.Databases.Add(d);
        d.Update(UpdateOptions.ExpandFull);
        d.Model.RequestRefresh(Tom.RefreshType.Full); d.Model.SaveChanges();
    }
}
