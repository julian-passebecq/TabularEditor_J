using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

// Before/after cancel probe. Drives the product executor (PbiBenchAmoDaxQueryExecutor, by reflection so the same
// probe runs against any build) and samples, every 250 ms from a separate connection, the DISCOVER_COMMANDS row of
// the probe query and the msmdsrv process CPU. args: binDir server db msmdsrvPid label log
internal static class P
{
    const string Fe = "EVALUATE ROW(\"fe\", COUNTROWS(FILTER(CROSSJOIN(SELECTCOLUMNS(GENERATESERIES(1,6000),\"a\",[Value]),SELECTCOLUMNS(GENERATESERIES(1,6000),\"b\",[Value])), MOD([a]*[b],7)=3)))";
    const string Se = "EVALUATE ROW(\"se\", SUMX(SELECTCOLUMNS(GENERATESERIES(1,2000),\"k\",[Value]), VAR k = [k] RETURN CALCULATE(COUNTROWS(Big), Big[V] > k * 1000, Big[G] <> MOD(k, 977))))";
    static string bin;
    static readonly List<Tuple<long, double>> cpuSamples = new List<Tuple<long, double>>();
    static long goneAt = -1;
    static readonly ManualResetEventSlim ready = new ManualResetEventSlim();
    static volatile object serverEndBox;
    static DateTime? serverEndTime { get => (DateTime?)serverEndBox; set => serverEndBox = value; }
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static StreamWriter log;
    static void L(string s) { var line = (clock.ElapsedMilliseconds / 1000.0).ToString("0.000").PadLeft(8) + " " + s; Console.WriteLine(line); log.WriteLine(line); log.Flush(); }

    static int Main(string[] a)
    {
        bin = Path.GetFullPath(a[0]);
        if (a.Length > 6) tomDirOverride = Path.GetFullPath(a[6]); // packaged builds embed TOMWrapper: take it from a build of the same commit
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            foreach (var dir in new[] { bin, TomDir() })
                foreach (var ext in new[] { ".dll", ".exe" }) { var f = Path.Combine(dir, name + ext); if (File.Exists(f)) return Assembly.LoadFrom(f); }
            return null;
        };
        return Run(a);
    }

    static int Run(string[] a)
    {
        string server = a[1], db = a[2]; int pid = int.Parse(a[3]); string label = a[4];
        log = new StreamWriter(a[5], false);
        if (db != "tmp_pbibench-cancel_20261016") { L("refusing " + db); return 2; }
        var te = Assembly.LoadFrom(Path.Combine(bin, "TabularEditor.exe"));
        var tomw = Assembly.LoadFrom(Path.Combine(TomDir(), "TOMWrapper.dll"));
        var core = Assembly.LoadFrom(Path.Combine(bin, "PbiBench.Core.dll"));
        L("build " + label + " TabularEditor.exe " + Sha(Path.Combine(bin, "TabularEditor.exe")));
        var admin = new Microsoft.AnalysisServices.Tabular.Server(); admin.Connect("Data Source=" + server);
        if (admin.Databases.ContainsName(db)) { L("exists, refusing"); return 3; }
        IDisposable handler = null;
        try
        {
            Create(admin, db); L("created " + db);
            var ctor = tomw.GetType("TabularEditor.TOMWrapper.TabularModelHandler").GetConstructors()
                .First(c => { var ps = c.GetParameters(); return ps.Length >= 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(string) && ps.Skip(2).All(x => x.IsOptional); });
            handler = (IDisposable)ctor.Invoke(new object[] { server, db }.Concat(ctor.GetParameters().Skip(2).Select(x => x.DefaultValue)).ToArray());
            var exType = te.GetType("TabularEditor.PbiBench.Dax.PbiBenchAmoDaxQueryExecutor");
            var args = new object[] { handler, null, null };
            if (!(bool)exType.GetMethod("TryCreate", BindingFlags.Public | BindingFlags.Static).Invoke(null, args)) { L("no executor " + args[2]); return 4; }
            var executor = args[1];
            var reqType = core.GetType("PbiBench.Core.Dax.DaxQueryRequest");
            var exec = exType.GetMethod("ExecuteAsync");
            foreach (var scen in new[] { "SE cancel@700ms", "SE timeout@1s", "FE cancel@700ms", "FE timeout@1s" })
            {
                var tag = "probe_" + Guid.NewGuid().ToString("N").Substring(0, 12);
                var q = (scen.StartsWith("SE") ? Se.Replace("\"se\"", "\"" + tag + "\"") : Fe.Replace("\"fe\"", "\"" + tag + "\""));
                L("tag " + tag);
                lock (cpuSamples) cpuSamples.Clear(); goneAt = -1;
                var stop = false; long end = -1; long firstSeen = -1; serverEndTime = null;
                var sampler = new Thread(() => Sample(server, pid, tag, () => stop, ref end, ref firstSeen)) { IsBackground = true };
                ready.Reset(); sampler.Start(); ready.Wait(60000); Thread.Sleep(500);
                L("=== " + scen);
                var req = Activator.CreateInstance(reqType);
                reqType.GetProperty("QueryText").SetValue(req, q);
                reqType.GetProperty("TimeoutSeconds").SetValue(req, scen.Contains("timeout") ? 1 : 120);
                long t0 = clock.ElapsedMilliseconds;
                object result;
                using (var cts = new CancellationTokenSource())
                {
                    if (scen.Contains("cancel")) cts.CancelAfter(700);
                    var task = (Task)exec.Invoke(executor, new object[] { req, cts.Token });
                    task.Wait();
                    result = task.GetType().GetProperty("Result").GetValue(task);
                }
                long t1 = clock.ElapsedMilliseconds; var clientWall = DateTime.UtcNow;
                var rt = result.GetType();
                var stopProp = rt.GetProperty("ServerStop");
                L(scen + ": CLIENT returned +" + (t1 - t0) + " ms state=" + rt.GetProperty("State").GetValue(result) + " reason=" + rt.GetProperty("Reason").GetValue(result) +
                    " serverStop=" + (stopProp == null ? "(not in this build)" : stopProp.GetValue(result) + " after " + rt.GetProperty("ServerStopMilliseconds").GetValue(result) + " ms") +
                    " message=\"" + rt.GetProperty("ErrorMessage").GetValue(result) + "\"");
                var waitStart = clock.ElapsedMilliseconds;
                while (Volatile.Read(ref end) < 0 && goneAt < 0 && clock.ElapsedMilliseconds - waitStart < 90000) Thread.Sleep(100);
                Thread.Sleep(2500); stop = true; sampler.Join(5000);
                var serverEnd = Volatile.Read(ref end);
                var endTime = serverEndTime;
                double after2s, before; lock (cpuSamples)
                {
                    var post = cpuSamples.Where(x => x.Item1 > t1 + 300 && x.Item1 <= t1 + 2300).Select(x => x.Item2).DefaultIfEmpty(-1).ToList();
                    var pre = cpuSamples.Where(x => x.Item1 >= t0 + 300 && x.Item1 < Math.Min(t1, t0 + 700)).Select(x => x.Item2).DefaultIfEmpty(-1).ToList();
                    after2s = post.Average(); before = pre.Average();
                }
                L(scen + ": msmdsrv CPU while running (before cancel) avg " + before.ToString("0.0") + "%, in the 2 s after the client returned avg " + after2s.ToString("0.0") + "%" +
                    (goneAt < 0 ? "" : "; command row gone from DISCOVER_COMMANDS at +" + (goneAt - t0) + " ms (client returned +" + (t1 - t0) + " ms)"));
                L(scen + ": SERVER COMMAND_END_TIME=" + (endTime == null ? "not captured" : endTime.Value.ToString("HH:mm:ss.fff")) + " UTC, client return " + clientWall.ToString("HH:mm:ss.fff") + " UTC" +
                    (endTime == null ? "" : " => server ended " + (int)(endTime.Value - clientWall).TotalMilliseconds + " ms relative to the client return (negative = before)"));
                L(scen + ": SERVER command end observed at +" + (serverEnd < 0 ? "never(90s)" : (serverEnd - t0).ToString()) + " ms; " +
                    (serverEnd < 0 ? "" : serverEnd <= t1 + 250 ? "server stopped before (or with) the client return" : "server kept running " + (serverEnd - t1) + " ms AFTER the client returned"));
                var r2 = Activator.CreateInstance(reqType); reqType.GetProperty("QueryText").SetValue(r2, "EVALUATE ROW(\"ok\", 1)");
                var t2 = (Task)exec.Invoke(executor, new object[] { r2, CancellationToken.None }); t2.Wait();
                var res2 = t2.GetType().GetProperty("Result").GetValue(t2);
                L(scen + ": executor reuse afterwards: " + res2.GetType().GetProperty("State").GetValue(res2));
            }
        }
        catch (Exception e) { L("ERROR " + e); }
        finally
        {
            try { handler?.Dispose(); } catch { }
            admin.Refresh(true);
            if (admin.Databases.ContainsName(db)) admin.Databases.GetByName(db).Drop();
            admin.Refresh(true); L("dropped exists=" + admin.Databases.ContainsName(db) + " dbs=" + admin.Databases.Count);
            admin.Disconnect();
        }
        return 0;
    }

    static string tomDirOverride;
    static string TomDir() => tomDirOverride ?? Path.GetFullPath(Path.Combine(bin, "..", "..", "..", "TOMWrapper", "bin", "Release"));

    static string Sha(string f) { using (var s = System.Security.Cryptography.SHA256.Create()) using (var fs = File.OpenRead(f)) return BitConverter.ToString(s.ComputeHash(fs)).Replace("-", "").ToLowerInvariant().Substring(0, 16); }

    static void Sample(string server, int pid, string marker, Func<bool> stop, ref long end, ref long firstSeen)
    {
        try
        {
            PerformanceCounter cpu = null;
            foreach (var inst in new PerformanceCounterCategory("Process").GetInstanceNames().Where(n => n.StartsWith("msmdsrv")))
                using (var idc = new PerformanceCounter("Process", "ID Process", inst, true))
                    if ((int)idc.RawValue == pid) cpu = new PerformanceCounter("Process", "% Processor Time", inst, true);
            cpu?.NextValue();
            using (var s = new Microsoft.AnalysisServices.Tabular.Server())
            {
                s.Connect("Data Source=" + server);
                while (!stop())
                {
                    string cmd = "none"; bool running = false; bool seen = false;
                    Microsoft.AnalysisServices.XmlaResultCollection res;
                    using (var r = s.ExecuteReader("<Statement>SELECT SESSION_SPID, COMMAND_ELAPSED_TIME_MS, COMMAND_CPU_TIME_MS, COMMAND_END_TIME, COMMAND_TEXT FROM $SYSTEM.DISCOVER_COMMANDS</Statement>", out res,
                        new Dictionary<string, string> { { "Format", "Tabular" } }))
                        while (r.Read())
                            if (Convert.ToString(r[4]).Contains(marker))
                            {
                                seen = true; running = r.IsDBNull(3);
                                if (!running && serverEndTime == null) serverEndTime = (DateTime)r[3];
                                cmd = "spid=" + r[0] + " elapsed=" + r[1] + " cpu=" + r[2] + " end=" + (running ? "-" : ((DateTime)r[3]).ToString("HH:mm:ss.fff"));
                            }
                    var now = clock.ElapsedMilliseconds;
                    if (seen && running && firstSeen < 0) firstSeen = now;
                    if (firstSeen >= 0 && !running && Volatile.Read(ref end) < 0) Volatile.Write(ref end, now);
                    if (firstSeen >= 0 && !seen && goneAt < 0) goneAt = now;
                    ready.Set();
                    var pct = cpu == null ? -1 : cpu.NextValue() / Environment.ProcessorCount;
                    lock (cpuSamples) cpuSamples.Add(Tuple.Create(now, (double)pct));
                    L("   dmv[" + cmd + "] msmdsrv cpu=" + (cpu == null ? "n/a" : pct.ToString("0.0") + "%"));
                    Thread.Sleep(100);
                }
            }
        }
        catch (Exception e) { L("sampler error " + e.Message); }
    }

    static void Create(Microsoft.AnalysisServices.Tabular.Server admin, string dbName)
    {
        var d = new Microsoft.AnalysisServices.Tabular.Database { Name = dbName, ID = dbName, CompatibilityLevel = 1600, StorageEngineUsed = Microsoft.AnalysisServices.StorageEngineUsed.TabularMetadata };
        d.Model = new Microsoft.AnalysisServices.Tabular.Model { Name = "Model" };
        var sales = new Microsoft.AnalysisServices.Tabular.Table { Name = "Sales" };
        sales.Partitions.Add(new Microsoft.AnalysisServices.Tabular.Partition { Name = "Sales", Source = new Microsoft.AnalysisServices.Tabular.CalculatedPartitionSource { Expression = "DATATABLE(\"Region\", STRING, \"Amount\", INTEGER, {{\"North\", 10}, {\"South\", 20}, {\"East\", 5}})" } });
        d.Model.Tables.Add(sales);
        var big = new Microsoft.AnalysisServices.Tabular.Table { Name = "Big" };
        big.Partitions.Add(new Microsoft.AnalysisServices.Tabular.Partition { Name = "Big", Source = new Microsoft.AnalysisServices.Tabular.CalculatedPartitionSource { Expression = "SELECTCOLUMNS(GENERATESERIES(1, 3000000), \"V\", [Value], \"G\", MOD([Value], 977))" } });
        d.Model.Tables.Add(big);
        admin.Databases.Add(d);
        d.Update(Microsoft.AnalysisServices.UpdateOptions.ExpandFull);
        d.Model.RequestRefresh(Microsoft.AnalysisServices.Tabular.RefreshType.Full); d.Model.SaveChanges();
    }
}
