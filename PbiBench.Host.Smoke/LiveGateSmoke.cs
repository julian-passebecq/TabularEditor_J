using System;
using System.Text.RegularExpressions;
using System.Threading;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;
using TabularEditor.TOMWrapper;
using Tom = Microsoft.AnalysisServices.Tabular;

// DAX-006 live gate (opt-in, never in CI). Runs only when PBIBENCH_LIVE_SERVER and PBIBENCH_LIVE_DATABASE are set.
// The database name must match tmp_te2_yyyymmdd (or the older te2_tmp_yyyymmdd) and must not exist yet: the gate creates it from synthetic
// DATATABLE rows, runs the real TE2 connection + PbiBench AMO executor against it, and drops it in finally.
// No other database on the server is opened, read or changed (only Databases.ContainsName on the target name).
internal static partial class SliceSmokes
{
    static partial void LateDax() => LiveGateSmoke.RunIfConfigured();
}

internal static class LiveGateSmoke
{
    internal static void RunIfConfigured()
    {
        var serverName = Environment.GetEnvironmentVariable("PBIBENCH_LIVE_SERVER");
        var dbName = Environment.GetEnvironmentVariable("PBIBENCH_LIVE_DATABASE");
        if (string.IsNullOrWhiteSpace(serverName) || string.IsNullOrWhiteSpace(dbName))
        { Console.WriteLine("DAX-006 live gate NOT_RUN (PBIBENCH_LIVE_SERVER/PBIBENCH_LIVE_DATABASE not set)"); return; }
        Check(Regex.IsMatch(dbName, @"^(tmp_te2|te2_tmp)_\d{8}$"), "database name must match tmp_te2_yyyymmdd");

        var admin = new Tom.Server();
        admin.Connect("Data Source=" + serverName);
        var created = false;
        TabularModelHandler handler = null;
        try
        {
            Check(!admin.Databases.ContainsName(dbName), "target database already exists; refusing to touch it");
            created = true; // from here on the database may exist partially: always drop it
            CreateFixture(admin, dbName);
            Console.WriteLine("DAX-006 live gate: throwaway database created");

            handler = new TabularModelHandler(serverName, dbName);
            Check(handler.IsConnected && handler.Model.Tables.Contains("Sales"), "TE2 loads the live model");
            Check(handler.Model.Tables["Sales"].Measures.Contains("Total Amount"), "TE2 sees the live measure");
            Check(PbiBenchAmoDaxQueryExecutor.TryCreate(handler, out var executor, out var reason), "executor available: " + reason);
            Check(executor.ConnectionLabel.EndsWith("/ " + dbName) && executor.ConnectionLabel.IndexOf("Data Source", StringComparison.OrdinalIgnoreCase) < 0,
                "connection label is server / database only");

            var multi = Run(executor, "EVALUATE Sales ORDER BY Sales[Amount] DESC EVALUATE ROW(\"Total\", [Total Amount])");
            Check(multi.State == DaxQueryExecutionState.Completed && multi.ResultSets.Length == 2,
                "two result sets (" + multi.State + ", " + multi.ResultSets.Length + ", " + multi.Reason + ")");
            Check(multi.ResultSets[0].ReturnedRowCount == 3 && Convert.ToInt64(multi.ResultSets[0].Rows[0][1]) == 20L, "first set: 3 rows, top amount 20");
            Check(Convert.ToInt64(multi.ResultSets[1].Rows[0][0]) == 35L, "measure total 35 through the live engine");

            var limited = Run(executor, "EVALUATE Sales", maxRows: 2);
            DaxResultSet limitedSet = limited.ResultSets.Length > 0 ? limited.ResultSets[0] : limited;
            Check(limitedSet.ReturnedRowCount == 2 && limitedSet.IsTruncated && limitedSet.Reason == DaxQueryReason.RowLimit, "row limit truncates with RowLimit");

            var bad = Run(executor, "EVALUATE NoSuchTable");
            Check(bad.State == DaxQueryExecutionState.Failed && bad.Reason == DaxQueryReason.Query && bad.ErrorMessage.IndexOf("NoSuchTable", StringComparison.Ordinal) < 0,
                "query error fails without echoing provider text (" + bad.State + ", " + bad.Reason + ")");

            const string slow = "EVALUATE ROW(\"n\", COUNTROWS(FILTER(CROSSJOIN(SELECTCOLUMNS(GENERATESERIES(1,6000),\"a\",[Value]),SELECTCOLUMNS(GENERATESERIES(1,6000),\"b\",[Value])), MOD([a]*[b],7)=3)))";
            using (var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700)))
            {
                var cancelled = executor.ExecuteAsync(new DaxQueryRequest { QueryText = slow }, cts.Token).GetAwaiter().GetResult();
                Check(cancelled.State == DaxQueryExecutionState.Cancelled && cancelled.Reason == DaxQueryReason.UserCancellation,
                    "user cancel reports Cancelled, never results (" + cancelled.State + ", " + cancelled.DurationMilliseconds + " ms)");
                Check(cancelled.DurationMilliseconds < 10000, "user cancel returns within 10 s through the session abort (" + cancelled.DurationMilliseconds + " ms)");
                Console.WriteLine("DAX-006 live cancel returned after " + cancelled.DurationMilliseconds + " ms");
            }
            var timedOut = Run(executor, slow, timeoutSeconds: 1);
            Check(timedOut.State == DaxQueryExecutionState.Cancelled && timedOut.Reason == DaxQueryReason.Timeout,
                "timeout reports Cancelled/Timeout (" + timedOut.State + ", " + timedOut.Reason + ", " + timedOut.DurationMilliseconds + " ms)");
            Check(timedOut.DurationMilliseconds < 10000, "timeout returns within 10 s through the session abort (" + timedOut.DurationMilliseconds + " ms)");
            Console.WriteLine("DAX-006 live timeout returned after " + timedOut.DurationMilliseconds + " ms");
            // The abort ends only the dedicated query session: the TE2 editor connection must still work.
            handler.Database.Refresh(true);
            Check(handler.Database.Server.Connected && handler.Database.Model.Tables.Contains("Sales"), "TE2 editor connection survives the query-session abort");

            var after = Run(executor, "EVALUATE ROW(\"ok\", 1)");
            Check(after.State == DaxQueryExecutionState.Completed, "executor usable after cancel and timeout");
            Console.WriteLine("DAX-006 live gate (multi-rowset, measure, row limit, query error, cancel, timeout, abort, editor connection, reuse) PASS");
        }
        finally
        {
            try { handler?.Dispose(); } catch (Exception) { }
            if (created)
            {
                admin.Refresh(true);
                if (admin.Databases.ContainsName(dbName)) admin.Databases.GetByName(dbName).Drop();
                admin.Refresh(true);
                Check(!admin.Databases.ContainsName(dbName), "throwaway database dropped");
                Console.WriteLine("DAX-006 live gate: throwaway database dropped");
            }
            admin.Disconnect(); admin.Dispose();
        }
    }

    private static void CreateFixture(Tom.Server admin, string dbName)
    {
        var db = new Tom.Database { Name = dbName, ID = dbName, CompatibilityLevel = 1600,
            StorageEngineUsed = Microsoft.AnalysisServices.StorageEngineUsed.TabularMetadata };
        db.Model = new Tom.Model { Name = "Model" };
        var sales = new Tom.Table { Name = "Sales" };
        sales.Partitions.Add(new Tom.Partition { Name = "Sales", Source = new Tom.CalculatedPartitionSource {
            Expression = "DATATABLE(\"Region\", STRING, \"Amount\", INTEGER, {{\"North\", 10}, {\"South\", 20}, {\"East\", 5}})" } });
        db.Model.Tables.Add(sales);
        admin.Databases.Add(db);
        db.Update(Microsoft.AnalysisServices.UpdateOptions.ExpandFull);
        db.Model.RequestRefresh(Tom.RefreshType.Full);
        db.Model.SaveChanges();
        sales.Measures.Add(new Tom.Measure { Name = "Total Amount", Expression = "SUM(Sales[Amount])" });
        db.Model.SaveChanges();
        db.Model.RequestRefresh(Tom.RefreshType.Calculate);
        db.Model.SaveChanges();
    }

    private static DaxQueryResult Run(PbiBenchAmoDaxQueryExecutor executor, string query, int maxRows = 5000, int timeoutSeconds = 60) =>
        executor.ExecuteAsync(new DaxQueryRequest { QueryText = query, MaxRows = maxRows, TimeoutSeconds = timeoutSeconds }, CancellationToken.None).GetAwaiter().GetResult();

    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Live gate: " + message); }
}
