using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;

internal static class MultipleResultsSmoke
{
    internal static void Run()
    {
        var session = new Session(2);
        var result = Execute(session);
        Check(result.ResultSets.Length == 2 && result.TotalReturnedRowCount == 2, "Secondary result lost.");
        Check((int)result.ResultSets[1].Rows[0][0] == 2 && (int)result.Rows[0][0] == 1, "Result identity.");
        Check(session.Disposed, "Session cleanup.");
        var rowLimited = new PbiBenchAmoDaxQueryExecutor(() => new Session(2, 2)).ExecuteAsync(
            new DaxQueryRequest { QueryText = "synthetic", MaxRows = 1 }, CancellationToken.None).GetAwaiter().GetResult();
        Check(rowLimited.ResultSets.Length == 2 && rowLimited.TotalReturnedRowCount == 2 &&
            rowLimited.ResultSets.All(x => x.IsTruncated && x.Reason == DaxQueryReason.RowLimit), "Per-set row limit or secondary traversal.");
        using (var cts = new CancellationTokenSource())
        {
            var cancelledSession = new QueryLifecycleSmoke.Session();
            cancelledSession.Reader.OnNextResult = cts.Cancel;
            var cancelled = new PbiBenchAmoDaxQueryExecutor(() => cancelledSession).ExecuteAsync(
                new DaxQueryRequest { QueryText = "synthetic" }, cts.Token).GetAwaiter().GetResult();
            Check(cancelled.State == DaxQueryExecutionState.Cancelled && cancelledSession.Disposes == 1, "Cancellation between sets/cleanup.");
        }
        var bounded = Execute(new Session(2), 226);
        Check(bounded.ResultSets.Length == 1 && bounded.RemainingResultsNotRead && bounded.Reason == DaxQueryReason.MemoryLimit,
            "Retained budget must be shared across sets.");
        var capped = Execute(new Session(33));
        Check(capped.ResultSets.Length == 32 && capped.RemainingResultsNotRead && capped.Reason == DaxQueryReason.ResultSetLimit, "Set cap.");
        var none = Execute(new Session(0));
        Check(none.State == DaxQueryExecutionState.Completed && none.ResultSets.Length == 0, "Zero results.");
        using (var form = new PbiBenchDaxWorkbenchForm(null, "", "Synthetic"))
        {
            var handle = form.Handle;
            var type = form.GetType();
            type.GetMethod("BindExecutionResult", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, new object[] { result });
            var selector = (ComboBox)type.GetField("_resultSelector", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            selector.SelectedIndex = 1;
            var selected = (DaxQueryResult)type.GetField("_currentResult", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            using (var writer = new StringWriter())
            {
                DaxCsvExport.Write(writer, selected);
                Check(writer.ToString().Contains("\"2\"") && !writer.ToString().Contains("\"1\""), "Export selected set.");
            }
        }
        var guarded = new Session(1);
        using (var form = new PbiBenchDaxWorkbenchForm(new PbiBenchAmoDaxQueryExecutor(() => guarded), "Synthetic", ""))
        {
            var handle = form.Handle;
            form.LoadPreviewDraft(DaxTablePreview.Build("Synthetic", 10), 10);
            form.ExecutionContextIsCurrent = () => false;
            form.GetType().GetMethod("ExecuteQuery", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, null);
            Check(!guarded.Executed, "Stale preview was executed.");
        }
        Console.WriteLine("Multiple results: zero/many, shared budget, set cap, selection/export and cleanup PASS");
    }
    private static DaxQueryResult Execute(Session session, long bytes = DaxQueryRequest.DefaultMaxRetainedBytes) =>
        new PbiBenchAmoDaxQueryExecutor(() => session).ExecuteAsync(new DaxQueryRequest { QueryText = "synthetic", MaxRetainedBytes = bytes }, CancellationToken.None).GetAwaiter().GetResult();
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class Session : IDaxQuerySession
    {
        private readonly int _count;
        private readonly int _rows;
        internal bool Disposed;
        internal bool Executed;
        internal Session(int count, int rows = 1) { _count = count; _rows = rows; }
        public void Connect(string connectionString, int seconds) { }
        public IDataReader Execute(string query, string catalog)
        {
            Executed = true;
            if (_count == 0) return null;
            var tables = Enumerable.Range(1, _count).Select(n => {
                var table = new DataTable(); table.Columns.Add("x", typeof(int));
                for (var i = 0; i < _rows; i++) table.Rows.Add(n);
                return table;
            }).ToArray();
            return new DataTableReader(tables);
        }
        public void Cancel() { }
        public void Abort() { }
        public DaxServerStopState WaitForServerStop(int timeoutMilliseconds) => DaxServerStopState.Confirmed;
        public void Disconnect() { }
        public void Dispose() { Disposed = true; }
    }
}
