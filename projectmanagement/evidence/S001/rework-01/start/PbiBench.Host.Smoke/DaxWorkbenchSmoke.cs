using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PbiBench.Core.Dax;

internal static class DaxWorkbenchSmoke
{
    public static void Run(Assembly host)
    {
        var provider = new QueryProvider();
        var type = host.GetType("TabularEditor.PbiBench.Dax.PbiBenchDaxWorkbenchForm", true);
        using (var form = (Form)Activator.CreateInstance(type, provider, "Test provider", ""))
        {
            var handle = form.Handle;
            var editor = Field<RichTextBox>(form, "_queryEditor");
            var history = Field<DaxQueryHistory>(form, "_history");
            var grid = Field<DataGridView>(form, "_results");
            var historyList = Field<ListView>(form, "_historyList");
            const string selected = "EVALUATE ROW(\"Selected\", 1)";
            editor.Text = selected + "\nEVALUATE ROW(\"Whole\", 2)";
            editor.Select(0, selected.Length);
            Assert(Field<Button>(form, "_executeButton").Text == "Execute selection", "Selection action label missing.");
            Field<NumericUpDown>(form, "_maxRows").Value = 2;
            Field<NumericUpDown>(form, "_timeoutSeconds").Value = 7;
            Invoke(form, "ExecuteQuery");
            WaitUntil(() => Field<CancellationTokenSource>(form, "_runCts") == null);
            Assert(provider.Request.QueryText == selected, "Execution ignored selected DAX.");
            Assert(provider.Request.MaxRows == 2 && provider.Request.TimeoutSeconds == 7, "Query bounds were lost.");
            Assert(history.Items[0].QueryText == selected && history.Items[0].State == DaxQueryExecutionState.Completed,
                "History did not record the executed selection.");
            Assert(grid.RowCount == 2 && history.Items[0].ReturnedRowCount == 2 && history.Items[0].IsTruncated,
                "Truncated result metadata was lost.");
            Assert(historyList.Items[0].SubItems[1].Text == "Completed" && historyList.Items[0].SubItems[3].Text == "2+",
                "Result state or truncation missing from history UI.");

            editor.Select(0, 0);
            Invoke(form, "ExecuteQuery");
            WaitUntil(() => Field<CancellationTokenSource>(form, "_runCts") == null);
            Assert(provider.Request.QueryText == editor.Text, "No selection should execute the document.");

            var calls = provider.Calls;
            editor.Select(selected.Length, 1);
            Invoke(form, "ExecuteQuery");
            Assert(provider.Calls == calls, "Whitespace selection executed the document.");

            editor.Select(0, 0);
            provider.WaitForCancellation = true;
            Invoke(form, "ExecuteQuery");
            Assert(!historyList.Enabled && !Field<Button>(form, "_exportButton").Enabled, "Running controls were not locked.");
            Invoke(form, "ExecuteQuery");
            Assert(provider.Calls == calls + 1, "A second execution started while running.");
            Invoke(form, "CancelQuery");
            WaitUntil(() => Field<CancellationTokenSource>(form, "_runCts") == null);
            Assert(history.Items[0].State == DaxQueryExecutionState.Cancelled, "Cancellation was recorded as failure.");
            Assert(historyList.Items[0].SubItems[1].Text == "Cancelled", "Cancelled history label missing.");
            Assert(grid.RowCount == 0 && !Field<Button>(form, "_exportButton").Enabled, "Cancelled query retained stale results.");
            Assert(!editor.ReadOnly && Field<Button>(form, "_executeButton").Enabled, "Editor did not recover after cancellation.");

            provider.WaitForCancellation = false;
            provider.ThrowUnexpected = true;
            Invoke(form, "ExecuteQuery");
            WaitUntil(() => Field<CancellationTokenSource>(form, "_runCts") == null);
            Assert(history.Items[0].State == DaxQueryExecutionState.Failed &&
                !history.Items[0].ErrorMessage.Contains("secret-test"), "Unexpected provider failure leaked raw details.");

            provider.ThrowUnexpected = false;
            provider.WaitForCancellation = true;
            Invoke(form, "ExecuteQuery");
            var closing = new FormClosingEventArgs(CloseReason.UserClosing, false);
            Invoke(form, "OnWorkbenchClosing", form, closing);
            Assert(closing.Cancel && !Field<bool>(form, "_closing"), "Closing a running query must preserve the draft while cancelling.");
            WaitUntil(() => Field<CancellationTokenSource>(form, "_runCts") == null);
            Assert(history.Items[0].State == DaxQueryExecutionState.Cancelled && !editor.ReadOnly,
                "Closing during execution did not cancel and recover the editor.");
        }
        Console.WriteLine("DAX Workbench: selection, results, history, cancellation and failure recovery PASS");
    }

    private static T Field<T>(Form form, string name) => (T)form.GetType()
        .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
    private static void Invoke(Form form, string name, params object[] args) => form.GetType()
        .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(form, args);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void WaitUntil(Func<bool> finished)
    {
        var timer = Stopwatch.StartNew();
        while (!finished())
        {
            if (timer.Elapsed > TimeSpan.FromSeconds(5)) throw new TimeoutException("Workbench did not complete.");
            Application.DoEvents();
            Thread.Sleep(1);
        }
    }

    private sealed class QueryProvider : IDaxQueryExecutor
    {
        public DaxQueryRequest Request;
        public int Calls;
        public bool WaitForCancellation;
        public bool ThrowUnexpected;
        public Task<DaxQueryResult> ExecuteAsync(DaxQueryRequest request, CancellationToken token)
        {
            Request = request;
            Calls++;
            if (ThrowUnexpected) throw new InvalidOperationException("secret-test endpoint and credentials");
            if (WaitForCancellation) return CancelledAsync(token);
            return Task.FromResult(new DaxQueryResult
            {
                Columns = new[] { "Value" },
                Rows = new[] { new object[] { 1 }, new object[] { 2 } },
                IsTruncated = true,
                DurationMilliseconds = 12
            });
        }
        private static async Task<DaxQueryResult> CancelledAsync(CancellationToken token)
        {
            await Task.Delay(Timeout.Infinite, token);
            throw new InvalidOperationException("Cancellation was not observed.");
        }
    }
}
