// PbiBench remote XMLA check (re-run on build 95fea60: every query goes through TryCreate(handler)). Loads the vault TE2 (TabularEditor.exe, PbiBench.Core.dll, TOMWrapper embedded in the
// exe) and runs it against a Power BI XMLA endpoint as a service principal. Input JSON on STDIN only:
//   { clientId, secret, tenantId, workspace, database, fixture, pkg, ref, out }
// Nothing secret is printed or written: every log line passes through Redact().
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

internal static class P
{
    static string Pkg, Ref; static bool busy;
    static StreamWriter log; static readonly Stopwatch clock = Stopwatch.StartNew();
    static string[] secrets = new string[0];
    internal static string Redact(string s) { if (s == null) return null; foreach (var x in secrets) if (!string.IsNullOrEmpty(x)) s = s.Replace(x, "<redacted>"); return s; }
    internal static void L(string s) { var line = (clock.ElapsedMilliseconds / 1000.0).ToString("0.00").PadLeft(7) + "s " + Redact(s); Console.WriteLine(line); log.WriteLine(line); log.Flush(); }

    [STAThread]
    static int Main()
    {
        var req = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(Console.In.ReadToEnd());
        string G(string k) => req.ContainsKey(k) ? Convert.ToString(req[k]) : null;
        secrets = new[] { G("secret"), G("clientId"), G("tenantId") };
        Pkg = G("pkg"); Ref = G("ref");
        Directory.CreateDirectory(G("out"));
        log = new StreamWriter(Path.Combine(G("out"), "remote-xmla.log"), false);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            if (busy) return null; busy = true;
            try
            {
                var n = new AssemblyName(e.Name).Name;
                foreach (var dir in new[] { Pkg, Ref })
                    foreach (var x in new[] { ".dll", ".exe" }) { var f = Path.Combine(dir, n + x); if (File.Exists(f)) return Assembly.LoadFrom(f); }
                return null;
            }
            finally { busy = false; }
        };
        if (!Regex.IsMatch(G("workspace") ?? "", @"^dpf-test-pbibench-\d{8}$")) { L("refusing workspace " + G("workspace")); return 2; }
        try { return Run.Go(G("clientId"), G("secret"), G("tenantId"), G("workspace"), G("database"), G("fixture"), G("out"), G("redeploy") == "True"); }
        catch (Exception e) { L("FATAL " + e.GetType().Name + ": " + e.Message + "\n" + e.StackTrace); return 1; }
        finally { log.Close(); }
    }

    internal static string Sha(string f) { using (var s = SHA256.Create()) using (var i = File.OpenRead(f)) return BitConverter.ToString(s.ComputeHash(i)).Replace("-", "").ToLower(); }
    internal static string Sha(byte[] b) { using (var s = SHA256.Create()) return BitConverter.ToString(s.ComputeHash(b)).Replace("-", "").ToLower(); }
}

internal static class Run
{
    static readonly string[] Queries = {
        "EVALUATE ROW(\"Total\", [Total Amount])",
        "EVALUATE SUMMARIZECOLUMNS(Sales[Region], \"Total\", [Total Amount]) ORDER BY Sales[Region]",
        "EVALUATE ROW(\"Rows\", COUNTROWS(Sales), \"Avg\", AVERAGE(Sales[Amount]), \"Max\", MAX(Sales[Amount]))",
        "EVALUATE ROW(\"Share North\", DIVIDE(CALCULATE([Total Amount], Sales[Region] = \"North\"), [Total Amount]))",
        "EVALUATE ADDCOLUMNS(VALUES(Sales[Region]), \"Rank\", RANKX(ALL(Sales[Region]), [Total Amount])) ORDER BY Sales[Region]",
        "EVALUATE ROW(\"Blank\", CALCULATE([Total Amount], Sales[Region] = \"West\"), \"Div0\", DIVIDE(1, 0), \"Txt\", CONCATENATEX(Sales, Sales[Region], \",\", Sales[Amount], ASC))" };
    const string Slow = "EVALUATE ROW(\"n\", COUNTROWS(FILTER(CROSSJOIN(SELECTCOLUMNS(GENERATESERIES(1,6000),\"a\",[Value]),SELECTCOLUMNS(GENERATESERIES(1,6000),\"b\",[Value])), MOD([a]*[b],7)=3)))";
    const string WriteName = "TE2 Remote Double";
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
    static void L(string s) => P.L(s);

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static int Go(string cid, string secret, string tid, string ws, string db, string fixture, string outDir, bool redeploy)
    {
        var cs = $"Data Source=powerbi://api.powerbi.com/v1.0/myorg/{ws};User ID=app:{cid}@{tid};Password={secret}";
        var checks = new List<object>(); int fails = 0;
        void Check(string id, bool ok, string detail) { checks.Add(new { id, result = ok ? "PASS" : "FAIL", detail = P.Redact(detail) }); if (!ok) fails++; L((ok ? "PASS " : "FAIL ") + id + ": " + detail); }

        // Loaded assemblies (proof that the vault bytes run).
        var te = typeof(TabularEditor.TOMWrapper.TabularModelHandler).Assembly;
        var ex = typeof(TabularEditor.PbiBench.Dax.PbiBenchAmoDaxQueryExecutor).Assembly;
        var core = typeof(PbiBench.Core.Dax.DaxQueryRequest).Assembly;
        var tom = typeof(Microsoft.AnalysisServices.Tabular.Server).Assembly;
        L("TabularEditor " + ex.GetName().Version + " " + P.Sha(ex.Location));
        L("PbiBench.Core " + core.GetName().Version + " " + P.Sha(core.Location));
        L("TOMWrapper " + te.GetName().Version + " " + (string.IsNullOrEmpty(te.Location) ? "(in-memory)" : P.Sha(te.Location)));
        L("Microsoft.AnalysisServices.Tabular " + tom.GetName().Version + " " + FileVersionInfo.GetVersionInfo(tom.Location).FileVersion);

        // 1. Deploy the fixture (setup, raw TOM from the vault package). The workspace is ours: replace on rerun.
        var t0 = clock();
        var srv = new Microsoft.AnalysisServices.Tabular.Server();
        srv.Connect(cs);
        L($"TOM connected in {clock() - t0} ms; server version {srv.Version}, product '{srv.ProductName}', edition {srv.Edition}, mode {srv.ServerMode}");
        var engine = new Dictionary<string, object> { ["serverVersion"] = srv.Version, ["productName"] = srv.ProductName, ["edition"] = srv.Edition.ToString(), ["serverMode"] = srv.ServerMode.ToString() };
        var existing = srv.Databases.FindByName(db);
        if (existing != null && !redeploy)
        {
            L("database exists, reused (deployed by an earlier run of this harness)");
            var left = existing.Model.Tables["Sales"].Measures.Find(WriteName);
            if (left != null) { L("removing leftover measure from an aborted run"); existing.Model.Tables["Sales"].Measures.Remove(left); existing.Model.SaveChanges(); }
            engine["databaseCompatibilityLevel"] = existing.CompatibilityLevel;
            Check("deploy", true, $"database '{db}' present (compat {existing.CompatibilityLevel}), not redeployed");
            srv.Disconnect();
            goto read;
        }
        if (existing != null) { L("database exists, dropping our copy for a clean deploy"); existing.Drop(); }
        var dbo = Microsoft.AnalysisServices.Tabular.TmdlSerializer.DeserializeDatabaseFromFolder(fixture);
        dbo.Name = db; dbo.ID = db;
        L($"fixture compat {dbo.CompatibilityLevel}, dataSourceVersion {dbo.Model.DefaultPowerBIDataSourceVersion} (kept as is)");
        srv.Databases.Add(dbo); dbo.Update(Microsoft.AnalysisServices.UpdateOptions.ExpandFull);
        dbo.Model.RequestRefresh(Microsoft.AnalysisServices.Tabular.RefreshType.Full); dbo.Model.SaveChanges();
        srv.Disconnect(); srv = new Microsoft.AnalysisServices.Tabular.Server(); srv.Connect(cs);
        var deployed = srv.Databases.FindByName(db);
        engine["databaseCompatibilityLevel"] = deployed?.CompatibilityLevel;
        Check("deploy", deployed != null, $"database '{db}' created and refreshed over XMLA as sp-writer (compat {deployed?.CompatibilityLevel})");
        srv.Disconnect();

        read:
        // 2. Vault TE2 reads the model over XMLA, and the same fixture from disk; compare signatures.
        // TOMWrapper keeps one handler singleton: read the disk fixture first and dispose it before the remote open.
        var local = new TabularEditor.TOMWrapper.TabularModelHandler(fixture);
        var localSig = Signature(local.Model);
        local.Dispose();
        t0 = clock();
        var handler = new TabularEditor.TOMWrapper.TabularModelHandler(cs, db);
        var openMs = clock() - t0;
        var remoteSig = Signature(handler.Model);
        var diff = localSig.Except(remoteSig).Select(x => "-" + x).Concat(remoteSig.Except(localSig).Select(x => "+" + x)).ToList();
        L($"TE2 handler connected in {openMs} ms: IsConnected={handler.IsConnected}, compat {handler.CompatibilityLevel}, source type {handler.SourceType}");
        foreach (var s in remoteSig) L("  remote " + s);
        Check("te2-read", handler.IsConnected && remoteSig.Count > 0, $"vault TE2 opened '{db}' over XMLA in {openMs} ms ({remoteSig.Count} signature lines)");
        Check("te2-model-equal", diff.Count == 0, diff.Count == 0 ? "remote model signature equals the fixture as read by the vault TE2 from disk" : "diff: " + string.Join("; ", diff));

        // 3. DAX oracle through the vault query executor (its own owned session cloned from the TE2 connection).
        if (!TabularEditor.PbiBench.Dax.PbiBenchAmoDaxQueryExecutor.TryCreate(handler, out var exec, out var why))
        {
            Check("executor", false, "TryCreate refused: " + why);
            File.WriteAllText(Path.Combine(outDir, "checks.json"), Json.Serialize(new { checks, engine }));
            handler.Dispose(); return 1;
        }
        L("executor: " + exec.ConnectionLabel);
        // What AMO reports for the live connection (keys only): Password is absent there, the executor must restore it.
        var b0 = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = handler.Database.Server.ConnectionString };
        L("Server.ConnectionString keys: " + string.Join(", ", b0.Keys.Cast<string>()));
        var p1 = exec.ExecuteAsync(new PbiBench.Core.Dax.DaxQueryRequest { QueryText = Queries[0], MaxRows = 1000, TimeoutSeconds = 120 }, CancellationToken.None).GetAwaiter().GetResult();
        Check("executor-product-path", p1.State == PbiBench.Core.Dax.DaxQueryExecutionState.Completed,
            $"TryCreate(handler) on the XMLA connection, Q1: state={p1.State} reason={p1.Reason} in {p1.DurationMilliseconds} ms{(p1.State == PbiBench.Core.Dax.DaxQueryExecutionState.Completed ? "" : " ('" + p1.ErrorMessage + "')")}; Server.ConnectionString keys: {string.Join(", ", b0.Keys.Cast<string>())}");
        var results = new List<object>(); int qerr = 0;
        for (int i = 0; i < Queries.Length; i++)
        {
            var r = exec.ExecuteAsync(new PbiBench.Core.Dax.DaxQueryRequest { QueryText = Queries[i], MaxRows = 1000, TimeoutSeconds = 120 }, CancellationToken.None).GetAwaiter().GetResult();
            var ok = r.State == PbiBench.Core.Dax.DaxQueryExecutionState.Completed;
            if (!ok) qerr++;
            L($"Q{i + 1}: state={r.State} reason={r.Reason} rows={r.ReturnedRowCount} {r.DurationMilliseconds} ms {(ok ? "" : P.Redact(r.ErrorMessage))}");
            results.Add(new Dictionary<string, object> { ["id"] = "Q" + (i + 1), ["query"] = Queries[i], ["columns"] = r.Columns, ["rows"] = r.Rows.Select(row => row.Select(Norm).ToArray()).ToArray(),
                ["error"] = ok ? null : P.Redact(r.State + "/" + r.Reason + ": " + r.ErrorMessage), ["durationMs"] = r.DurationMilliseconds });
        }
        File.WriteAllText(Path.Combine(outDir, "results-remote.json"), Json.Serialize(results));
        Check("oracle-run", qerr == 0, $"{Queries.Length - qerr}/{Queries.Length} queries completed through the product executor from TryCreate(handler)");

        // 4. Timeout on the remote endpoint (informational: the client must come back; the engine side is not observable here).
        t0 = clock();
        var slow = exec.ExecuteAsync(new PbiBench.Core.Dax.DaxQueryRequest { QueryText = Slow, MaxRows = 10, TimeoutSeconds = 1 }, CancellationToken.None).GetAwaiter().GetResult();
        var slowMs = clock() - t0;
        var after = exec.ExecuteAsync(new PbiBench.Core.Dax.DaxQueryRequest { QueryText = "EVALUATE ROW(\"ok\", 1)", MaxRows = 10, TimeoutSeconds = 60 }, CancellationToken.None).GetAwaiter().GetResult();
        Check("timeout-client", slowMs < 10000 && after.State == PbiBench.Core.Dax.DaxQueryExecutionState.Completed,
            $"1 s timeout: client returned in {slowMs} ms state={slow.State} reason={slow.Reason}; executor reusable: {after.State}");

        // 5. Reversible write through TE2 (add measure, SaveDB, query, delete, SaveDB, verify absent with a fresh connection).
        var sales = handler.Model.Tables["Sales"];
        sales.AddMeasure(WriteName, "[Total Amount] * 2");
        try { handler.SaveDB(); }
        catch (Exception e) { Check("te2-write-save", false, "first SaveDB: " + e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
        var w = exec.ExecuteAsync(new PbiBench.Core.Dax.DaxQueryRequest { QueryText = $"EVALUATE ROW(\"d\", [{WriteName}])", MaxRows = 10, TimeoutSeconds = 60 }, CancellationToken.None).GetAwaiter().GetResult();
        var wv = w.State == PbiBench.Core.Dax.DaxQueryExecutionState.Completed && w.Rows.Length == 1 ? Convert.ToString(w.Rows[0][0]) : "(" + w.State + "/" + w.Reason + ")";
        Check("te2-write", wv == "70", $"TE2 SaveDB added '{WriteName}'; query over XMLA returned {wv}");
        sales.Measures[WriteName].Delete();
        try { handler.SaveDB(); }
        catch (Exception e) { Check("te2-rollback-save", false, "second SaveDB: " + e.GetType().Name + ": " + e.Message.Split('\n')[0] + " at " + e.StackTrace.Split('\n')[2].Trim()); }
        handler.Dispose();
        var verify = new Microsoft.AnalysisServices.Tabular.Server(); verify.Connect(cs);
        var vdb = verify.Databases.FindByName(db);
        var gone = vdb != null && !vdb.Model.Tables["Sales"].Measures.ContainsName(WriteName);
        var measures = vdb == null ? "" : string.Join(", ", vdb.Model.Tables["Sales"].Measures.Select(m => m.Name));
        verify.Disconnect();
        Check("te2-rollback", gone, $"after delete + SaveDB a fresh TOM connection sees Sales measures: [{measures}]");

        File.WriteAllText(Path.Combine(outDir, "checks.json"), Json.Serialize(new { checks, engine, remoteSignature = remoteSig, localSignature = localSig }));
        L(fails == 0 ? "ALL PASS" : fails + " FAIL");
        return fails == 0 ? 0 : 1;
    }

    static long clock() => (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
    static object Norm(object v) => v is DBNull ? null : v is decimal d ? (object)(double)d : v;

    static List<string> Signature(TabularEditor.TOMWrapper.Model m)
    {
        var s = new List<string>();
        foreach (var t in m.Tables.OrderBy(t => t.Name))
        {
            s.Add($"table {t.Name} ({t.GetType().Name})" + (t is TabularEditor.TOMWrapper.CalculatedTable ct ? " = " + ct.Expression : ""));
            foreach (var c in t.Columns.OrderBy(c => c.Name)) s.Add($"column {t.Name}[{c.Name}] {c.DataType}");
            foreach (var x in t.Measures.OrderBy(x => x.Name)) s.Add($"measure {t.Name}[{x.Name}] = {x.Expression}");
        }
        return s;
    }
}
