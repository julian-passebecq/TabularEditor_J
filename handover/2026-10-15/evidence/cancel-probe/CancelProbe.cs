using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Security;
using System.Threading;
using Microsoft.AnalysisServices;
using Tom = Microsoft.AnalysisServices.Tabular;

public static class CancelProbe
{
    public static void Log(string s) { Console.WriteLine(DateTime.Now.ToString("HH:mm:ss.fff") + " " + s); }

    public static void CreateDb(string server, string db)
    {
        var admin = new Tom.Server(); admin.Connect("Data Source=" + server);
        try
        {
            if (admin.Databases.ContainsName(db)) throw new Exception("exists: " + db);
            var d = new Tom.Database { Name = db, ID = db, CompatibilityLevel = 1600, StorageEngineUsed = StorageEngineUsed.TabularMetadata };
            d.Model = new Tom.Model { Name = "Model" };
            var t = new Tom.Table { Name = "Sales" };
            t.Partitions.Add(new Tom.Partition { Name = "Sales", Source = new Tom.CalculatedPartitionSource {
                Expression = "DATATABLE(\"Region\", STRING, \"Amount\", INTEGER, {{\"North\", 10}, {\"South\", 20}, {\"East\", 5}})" } });
            d.Model.Tables.Add(t);
            admin.Databases.Add(d);
            d.Update(UpdateOptions.ExpandFull);
            d.Model.RequestRefresh(Tom.RefreshType.Full); d.Model.SaveChanges();
            t.Measures.Add(new Tom.Measure { Name = "Total Amount", Expression = "SUM(Sales[Amount])" });
            d.Model.SaveChanges();
            d.Model.RequestRefresh(Tom.RefreshType.Calculate); d.Model.SaveChanges();
            Log("created " + db);
        }
        finally { admin.Disconnect(); admin.Dispose(); }
    }

    public static void DropDb(string server, string db)
    {
        var admin = new Tom.Server(); admin.Connect("Data Source=" + server);
        try
        {
            admin.Refresh(true);
            if (admin.Databases.ContainsName(db)) admin.Databases.GetByName(db).Drop();
            admin.Refresh(true);
            Log("dropped " + db + " stillExists=" + admin.Databases.ContainsName(db));
        }
        finally { admin.Disconnect(); admin.Dispose(); }
    }

    static string SessionInfo(Server admin, string sessionId)
    {
        var dmv = "SELECT SESSION_SPID, SESSION_STATUS, SESSION_LAST_COMMAND_ELAPSED_TIME_MS FROM $SYSTEM.DISCOVER_SESSIONS WHERE SESSION_ID = '" + sessionId + "'";
        XmlaResultCollection r;
        using (var reader = admin.ExecuteReader("<Statement>" + SecurityElement.Escape(dmv) + "</Statement>", out r, new Dictionary<string, string> { { "Format", "Tabular" } }))
        {
            if (reader != null && reader.Read()) return reader.GetValue(0) + "|" + reader.GetValue(1) + "|" + reader.GetValue(2);
        }
        return "gone";
    }

    public static void Run(string server, string db, string query, string method, int cancelAfterMs)
    {
        var admin = new Server(); admin.Connect("Data Source=" + server);
        var q = new Server(); q.Connect("Data Source=" + server + ";Initial Catalog=" + db);
        var sid = q.SessionID;
        var info0 = SessionInfo(admin, sid);
        var spid = info0.Split('|')[0];
        Log("method=" + method + " session=" + sid + " spid=" + spid);
        var watch = Stopwatch.StartNew();
        long returned = -1; string outcome = "";
        var t = new Thread(() =>
        {
            try
            {
                XmlaResultCollection res;
                var reader = q.ExecuteReader("<Statement>" + SecurityElement.Escape(query) + "</Statement>", out res,
                    new Dictionary<string, string> { { "Catalog", db }, { "Format", "Tabular" } });
                bool err = false;
                if (res != null) foreach (XmlaResult x in res) foreach (XmlaMessage m in x.Messages) if (m is XmlaError) { err = true; outcome = "xmlaError:" + m.Description; }
                if (reader != null) { while (reader.Read()) outcome += " row=" + reader.GetValue(0); reader.Dispose(); }
                if (!err && outcome == "") outcome = "empty";
            }
            catch (Exception ex) { outcome = "exception:" + ex.GetType().Name + ":" + ex.Message.Replace("\r", " ").Replace("\n", " "); }
            returned = watch.ElapsedMilliseconds;
        });
        t.Start();
        long cancelAt = -1;
        if (method != "none")
        {
            Thread.Sleep(cancelAfterMs);
            cancelAt = watch.ElapsedMilliseconds;
            Log("status before cancel: " + SessionInfo(admin, sid));
            try
            {
                switch (method)
                {
                    case "amo": q.CancelCommand(); break;
                    case "spid": admin.Execute("<Cancel xmlns=\"http://schemas.microsoft.com/analysisservices/2003/engine\"><SPID>" + spid + "</SPID></Cancel>"); break;
                    case "session": admin.Execute("<Cancel xmlns=\"http://schemas.microsoft.com/analysisservices/2003/engine\"><SessionID>" + sid + "</SessionID></Cancel>"); break;
                    case "spidassoc": admin.Execute("<Cancel xmlns=\"http://schemas.microsoft.com/analysisservices/2003/engine\"><SPID>" + spid + "</SPID><CancelAssociated>true</CancelAssociated></Cancel>"); break;
                    case "endsession": admin.Execute("<Cancel xmlns=\"http://schemas.microsoft.com/analysisservices/2003/engine\"><SessionID>" + sid + "</SessionID><CancelAssociated>true</CancelAssociated></Cancel>"); break;
                }
                Log("cancel sent at " + cancelAt + " ms (" + method + ")");
            }
            catch (Exception ex) { Log("cancel threw " + ex.GetType().Name + ": " + ex.Message); }
        }
        // Poll engine state while the query thread runs and up to 5 s after it returns.
        // Engine activity = CPU time of the SSAS instance process (only this probe queries the instance).
        long engineIdleAt = -1;
        var pidText = Environment.GetEnvironmentVariable("PROBE_SSAS_PID");
        PerformanceCounter proc = null;
        if (!string.IsNullOrEmpty(pidText))
            foreach (var inst in new PerformanceCounterCategory("Process").GetInstanceNames())
                if (inst.StartsWith("msmdsrv")) using (var idc = new PerformanceCounter("Process", "ID Process", inst, true)) if ((int)idc.RawValue == int.Parse(pidText)) { proc = new PerformanceCounter("Process", "% Processor Time", inst, true); proc.NextValue(); }
        int idleSamples = 0;
        while (true)
        {
            Thread.Sleep(500);
            if (proc != null)
            {
                var used = proc.NextValue();
                if (used < 15) { if (++idleSamples == 2 && engineIdleAt < 0) engineIdleAt = watch.ElapsedMilliseconds - 1000; }
                else { idleSamples = 0; engineIdleAt = -1; }
            }
            if (!t.IsAlive && engineIdleAt >= 0) break;
            if (watch.ElapsedMilliseconds > 600000) break;
        }
        t.Join();
        Log("RESULT method=" + method + " cancelAt=" + cancelAt + " clientReturned=" + returned + " engineIdleAt=" + engineIdleAt + " outcome=" + outcome);
        try { q.Disconnect(); } catch { }
        admin.Disconnect();
    }
}

