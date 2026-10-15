using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;
using TabularEditor.TOMWrapper;
using Tom = Microsoft.AnalysisServices.Tabular;
using Adomd = Microsoft.AnalysisServices.AdomdClient;

internal static class P
{
    const string Slow = "EVALUATE ROW(\"n\", COUNTROWS(FILTER(CROSSJOIN(SELECTCOLUMNS(GENERATESERIES(1,6000),\"a\",[Value]),SELECTCOLUMNS(GENERATESERIES(1,6000),\"b\",[Value])), MOD([a]*[b],7)=3)))";
    static StreamWriter log;
    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    static void L(string s) { var line = (clock.ElapsedMilliseconds / 1000.0).ToString("0.00").PadLeft(7) + "s " + s; Console.WriteLine(line); log.WriteLine(line); log.Flush(); }

    static volatile bool stop;
    static long firstSeen = -1, lastSeen = -1; static string lastState = "";
    static int samples;

    static int Main(string[] a)
    {
        string server = a[0], db = a[1]; log = new StreamWriter(a[2], false);
        if (db != "tmp_pbibench-pilot_20261015") { L("refusing db " + db); return 2; }
        var admin = new Tom.Server(); admin.Connect("Data Source=" + server);
        bool created = false; TabularModelHandler handler = null; int rc = 0;
        try
        {
            if (admin.Databases.ContainsName(db)) { L("db exists, refusing"); return 3; }
            created = true; CreateFixture(admin, db); L("created " + db);
            handler = new TabularModelHandler(server, db);
            PbiBenchAmoDaxQueryExecutor ex; string reason; if (!PbiBenchAmoDaxQueryExecutor.TryCreate(handler, out ex, out reason)) { L("no executor " + reason); return 4; }
            foreach (var scen in new[] { "user-cancel@700ms", "timeout@1s" })
            {
                stop = false; firstSeen = lastSeen = -1; lastState = ""; samples = 0;
                var poll = new Thread(() => Poll(server)) { IsBackground = true }; poll.Start();
                Thread.Sleep(600);
                long t0 = clock.ElapsedMilliseconds;
                DaxQueryResult r;
                if (scen.StartsWith("user"))
                {
                    using (var cts = new CancellationTokenSource(700))
                        r = ex.ExecuteAsync(new DaxQueryRequest { QueryText = Slow }, cts.Token).GetAwaiter().GetResult();
                }
                else
                    r = ex.ExecuteAsync(new DaxQueryRequest { QueryText = Slow, MaxRows = 5000, TimeoutSeconds = 1 }, CancellationToken.None).GetAwaiter().GetResult();
                long t1 = clock.ElapsedMilliseconds;
                L(scen + ": client returned state=" + r.State + " reason=" + r.Reason + " productDuration=" + r.DurationMilliseconds + "ms wall=" + (t1 - t0) + "ms (client within 10s: " + ((t1 - t0) < 10000) + ")");
                long waitStart = clock.ElapsedMilliseconds;
                while (clock.ElapsedMilliseconds - waitStart < 150000)
                {
                    Thread.Sleep(500);
                    if (lastSeen >= 0 && clock.ElapsedMilliseconds - lastSeen > 3000) break;
                    if (firstSeen < 0 && clock.ElapsedMilliseconds - waitStart > 8000) break;
                }
                bool stopped = lastSeen >= 0 && clock.ElapsedMilliseconds - lastSeen > 3000;
                stop = true; poll.Join(3000);
                if (firstSeen < 0) L(scen + ": slow command never observed in DISCOVER_COMMANDS (samples=" + samples + ")");
                else
                {
                    L(scen + ": engine command first seen at +" + (firstSeen - t0) + "ms, last seen at +" + (lastSeen - t0) + "ms relative to execute start (samples=" + samples + ", last " + lastState + ")");
                    long after = lastSeen - t1;
                    L(scen + ": engine kept executing " + Math.Max(0, after) + "ms after the client returned; stopped=" + stopped + "; server-side within 10 s: " + (stopped && after <= 10000));
                }
                var after1 = ex.ExecuteAsync(new DaxQueryRequest { QueryText = "EVALUATE ROW(\"ok\", 1)", MaxRows = 10, TimeoutSeconds = 60 }, CancellationToken.None).GetAwaiter().GetResult();
                L(scen + ": executor reusable afterwards: " + after1.State);
            }
        }
        catch (Exception e) { L("ERROR " + e.GetType().Name + ": " + e.Message); rc = 1; }
        finally
        {
            stop = true;
            try { if (handler != null) handler.Dispose(); } catch { }
            if (created)
            {
                try { admin.Refresh(true); if (admin.Databases.ContainsName(db)) admin.Databases.GetByName(db).Drop(); admin.Refresh(true); L("dropped; exists=" + admin.Databases.ContainsName(db)); }
                catch (Exception e) { L("DROP ERROR " + e.Message); rc = 1; }
            }
            admin.Disconnect();
        }
        return rc;
    }

    static void Poll(string server)
    {
        try
        {
            using (var cn = new Adomd.AdomdConnection("Data Source=" + server))
            {
                cn.Open();
                while (!stop)
                {
                    try
                    {
                        var cmd = cn.CreateCommand(); cmd.CommandText = "SELECT COMMAND_TEXT, COMMAND_ELAPSED_TIME_MS, COMMAND_CPU_TIME_MS FROM $SYSTEM.DISCOVER_COMMANDS";
                        using (var r = cmd.ExecuteReader())
                        {
                            bool seen = false;
                            while (r.Read())
                            {
                                var t = Convert.ToString(r["COMMAND_TEXT"]);
                                if (t != null && t.Contains("GENERATESERIES(1,6000)")) { seen = true; lastState = "elapsed=" + r["COMMAND_ELAPSED_TIME_MS"] + "ms cpu=" + r["COMMAND_CPU_TIME_MS"] + "ms"; }
                            }
                            samples++;
                            if (seen) { long now = clock.ElapsedMilliseconds; if (firstSeen < 0) firstSeen = now; lastSeen = now; }
                        }
                    }
                    catch (Exception e) { lastState = "poll error " + e.Message.Substring(0, Math.Min(60, e.Message.Length)); }
                    Thread.Sleep(250);
                }
            }
        }
        catch (Exception e) { Console.WriteLine("poller failed: " + e.Message); }
    }

    static void CreateFixture(Tom.Server admin, string dbName)
    {
        var d = new Tom.Database { Name = dbName, ID = dbName, CompatibilityLevel = 1600, StorageEngineUsed = Microsoft.AnalysisServices.StorageEngineUsed.TabularMetadata };
        d.Model = new Tom.Model { Name = "Model" };
        var sales = new Tom.Table { Name = "Sales" };
        sales.Partitions.Add(new Tom.Partition { Name = "Sales", Source = new Tom.CalculatedPartitionSource { Expression = "DATATABLE(\"Region\", STRING, \"Amount\", INTEGER, {{\"North\", 10}, {\"South\", 20}, {\"East\", 5}})" } });
        d.Model.Tables.Add(sales);
        admin.Databases.Add(d);
        d.Update(Microsoft.AnalysisServices.UpdateOptions.ExpandFull);
        d.Model.RequestRefresh(Tom.RefreshType.Full); d.Model.SaveChanges();
        sales.Measures.Add(new Tom.Measure { Name = "Total Amount", Expression = "SUM(Sales[Amount])" }); d.Model.SaveChanges();
        d.Model.RequestRefresh(Tom.RefreshType.Calculate); d.Model.SaveChanges();
    }
}

