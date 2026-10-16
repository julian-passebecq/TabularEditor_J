using System;
using System.IO;
using System.Linq;
using System.Threading;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;
using TabularEditor.TOMWrapper;
using Tom = Microsoft.AnalysisServices.Tabular;

// Power BI Desktop gate (opt-in, never in CI). Runs only when PBIBENCH_DESKTOP_SERVER is set to the local
// Analysis Services endpoint of a running Power BI Desktop (localhost:<port>) that has the synthetic fixture
// handover/2026-10-15/fixture/Gate.pbip open (a disposable copy, never a user file).
//   PBIBENCH_DESKTOP_PHASE=live (default): TE2 connects, adds the measure "TE2 Gate Double", saves to Desktop,
//     queries it through the PbiBench executor, cancels a slow query, and prints the DAX Studio handoff plan.
//   PBIBENCH_DESKTOP_PHASE=disk: after Desktop saved the project, TE2 loads PBIBENCH_DESKTOP_TMDL (the
//     SemanticModel/definition folder) from disk and must find the measure written through the live session.
internal static class DesktopGateSmoke
{
    internal const string MeasureName = "TE2 Gate Double";

    internal static void RunIfConfigured()
    {
        var server = Environment.GetEnvironmentVariable("PBIBENCH_DESKTOP_SERVER");
        if (string.IsNullOrWhiteSpace(server)) { Console.WriteLine("Power BI Desktop gate NOT_RUN (PBIBENCH_DESKTOP_SERVER not set)"); return; }
        var phase = Environment.GetEnvironmentVariable("PBIBENCH_DESKTOP_PHASE") ?? "live";
        if (phase == "disk") Disk(Environment.GetEnvironmentVariable("PBIBENCH_DESKTOP_TMDL"));
        else Live(server);
    }

    private static void Live(string server)
    {
        Check(server.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase), "Desktop endpoint must be localhost:<port>");
        string dbName;
        var probe = new Tom.Server();
        probe.Connect("Data Source=" + server);
        try
        {
            Check(probe.Databases.Count == 1, "a Power BI Desktop instance hosts exactly one model");
            dbName = probe.Databases[0].Name;
            var fixture = probe.Databases[0].Model;
            Check(fixture.Tables.ContainsName("Sales") && fixture.Tables["Sales"].Measures.ContainsName("Total Amount"),
                "the open model is the synthetic Gate fixture");
            // Fixture preparation: Desktop opens a project without cached data and asks for a manual refresh of
            // calculated tables. Recalculate the synthetic DATATABLE so queries return rows.
            fixture.RequestRefresh(Tom.RefreshType.Calculate);
            fixture.SaveChanges();
        }
        finally { probe.Disconnect(); probe.Dispose(); }

        using (var handler = new TabularModelHandler(server, dbName))
        {
            Check(handler.IsConnected && handler.IsPbiDesktop, "TE2 connects to Power BI Desktop (" + handler.Database?.CompatibilityLevel + ")");
            var sales = handler.Model.Tables["Sales"];
            Check(sales.Measures.Contains("Total Amount"), "TE2 sees the Desktop measure");
            if (sales.Measures.Contains(MeasureName)) sales.Measures[MeasureName].Delete();
            var added = sales.AddMeasure(MeasureName, "[Total Amount] * 2");
            handler.SaveDB();
            Console.WriteLine("Power BI Desktop gate: TE2 saved measure '" + MeasureName + "' to the Desktop model");

            Check(PbiBenchAmoDaxQueryExecutor.TryCreate(handler, out var executor, out var reason), "executor available: " + reason);
            var multi = executor.ExecuteAsync(new DaxQueryRequest { QueryText =
                "EVALUATE Sales ORDER BY Sales[Amount] DESC EVALUATE ROW(\"Total\", [Total Amount], \"Double\", [" + MeasureName + "])" },
                CancellationToken.None).GetAwaiter().GetResult();
            Check(multi.State == DaxQueryExecutionState.Completed && multi.ResultSets.Length == 2, "two result sets from Desktop (" + multi.State + ", " + multi.Reason + ")");
            Check(multi.ResultSets[0].ReturnedRowCount == 3, "three fixture rows");
            Check(Convert.ToInt64(multi.ResultSets[1].Rows[0][0]) == 35L && Convert.ToInt64(multi.ResultSets[1].Rows[0][1]) == 70L,
                "Desktop engine computes Total 35 and the TE2 measure 70");
            Console.WriteLine("Power BI Desktop gate: query through the PbiBench executor returned Total=35, Double=70");

            const string slow = "EVALUATE ROW(\"n\", COUNTROWS(FILTER(CROSSJOIN(SELECTCOLUMNS(GENERATESERIES(1,6000),\"a\",[Value]),SELECTCOLUMNS(GENERATESERIES(1,6000),\"b\",[Value])), MOD([a]*[b],7)=3)))";
            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700)))
            {
                var cancelled = executor.ExecuteAsync(new DaxQueryRequest { QueryText = slow }, cts.Token).GetAwaiter().GetResult();
                // The run now returns only once the engine is seen to stop (or after the 30 s stop wait).
                Check(cancelled.State == DaxQueryExecutionState.Cancelled && cancelled.Reason == DaxQueryReason.UserCancellation &&
                    cancelled.ServerStop == DaxServerStopState.Confirmed && cancelled.DurationMilliseconds < 40000,
                    "Desktop cancel (" + cancelled.State + ", " + cancelled.Reason + ", " + cancelled.ServerStop + ", " + cancelled.DurationMilliseconds + " ms)");
                Console.WriteLine("Power BI Desktop gate: cancel returned after " + cancelled.DurationMilliseconds + " ms, server stop " +
                    cancelled.ServerStop + " after " + cancelled.ServerStopMilliseconds + " ms");
            }
            var after = executor.ExecuteAsync(new DaxQueryRequest { QueryText = "EVALUATE ROW(\"ok\", [" + MeasureName + "])" }, CancellationToken.None).GetAwaiter().GetResult();
            Check(after.State == DaxQueryExecutionState.Completed, "executor usable after cancel");
            handler.Database.Refresh(true);
            Check(handler.Database.Server.Connected, "TE2 editor connection survives the query-session abort");

            var plan = DaxStudioHandoff.Plan(@"C:\Program Files\DAX Studio\DaxStudio.exe", new DaxStudioTarget(server, dbName));
            Check(plan.Kind == DaxStudioHandoffKind.LaunchConnected && plan.Arguments.Count == 2, "DAX Studio handoff plan: " + plan.Reason);
            Console.WriteLine("DAXSTUDIO_ARGS\t" + string.Join("\t", plan.Arguments));
            Console.WriteLine("Power BI Desktop gate (live: connect, write measure, query, cancel, editor connection, handoff plan) PASS");
        }
    }

    private static void Disk(string tmdlFolder)
    {
        Check(!string.IsNullOrWhiteSpace(tmdlFolder) && Directory.Exists(tmdlFolder), "PBIBENCH_DESKTOP_TMDL must be the saved SemanticModel/definition folder");
        using (var handler = new TabularModelHandler(tmdlFolder))
        {
            var sales = handler.Model.Tables["Sales"];
            Check(sales.Measures.Contains(MeasureName) && sales.Measures[MeasureName].Expression.Contains("[Total Amount] * 2"),
                "the measure written by TE2 through Desktop is in the saved TMDL");
            Console.WriteLine("Power BI Desktop gate (disk: TE2 reloads the project Desktop saved; measure present) PASS");
        }
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Desktop gate: " + message); }
}
