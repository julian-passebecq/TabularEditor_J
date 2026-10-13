using System;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;

internal static class QueryLifecycleSmoke
{
    internal static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    internal static void Wait(Func<bool> predicate)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate()) { Application.DoEvents(); if (watch.ElapsedMilliseconds > 10000) throw new Exception("Controlled test watchdog expired."); Thread.Sleep(1); }
    }
    private static DaxQueryResult Run(Session session, DaxQueryRequest request = null)
    {
        var task = new PbiBenchAmoDaxQueryExecutor(() => session).ExecuteAsync(request ?? new DaxQueryRequest { QueryText = "EVALUATE ROW(\"x\",1)" }, CancellationToken.None);
        Wait(() => task.IsCompleted);
        Assert(session.Disconnects == 1 && session.Disposes == 1, "Session cleanup must have one owner.");
        return task.GetAwaiter().GetResult();
    }
    internal static void Run()
    {
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); var factories = 0;
            var task = new PbiBenchAmoDaxQueryExecutor(() => { factories++; return new Session(); })
                .ExecuteAsync(new DaxQueryRequest { QueryText = "synthetic" }, cancelled.Token);
            Wait(() => task.IsCompleted);
            Assert(task.Result.Reason == DaxQueryReason.UserCancellation && factories == 0, "Pre-cancel opened a session.");
        }
        foreach (var phase in new[] { "connect", "execute", "read" })
        using (var release = new ManualResetEventSlim())
        using (var entered = new ManualResetEventSlim())
        using (var cancelEntered = new ManualResetEventSlim())
        using (var releaseCancel = new ManualResetEventSlim())
        using (var user = new CancellationTokenSource())
        {
            Action block = () => { entered.Set(); Assert(release.Wait(10000), "Blocked operation not released."); };
            var session = new Session();
            if (phase == "connect") session.OnConnect = block;
            if (phase == "execute") session.OnExecute = block;
            if (phase == "read") session.Reader.OnRead = block;
            session.OnCancel = () => { cancelEntered.Set(); Assert(releaseCancel.Wait(10000), "Cancel not released."); };
            var executor = new PbiBenchAmoDaxQueryExecutor(() => session);
            var request = new DaxQueryRequest { QueryText = "captured", TimeoutSeconds = 10 };
            var task = executor.ExecuteAsync(request, user.Token);
            Wait(() => entered.IsSet);
            request.QueryText = "mutated";
            user.Cancel(); user.Cancel(); // Must return even while Cancel() is deliberately blocked.
            var messageHandled = false;
            using (var control = new Control()) { var handle = control.Handle; control.BeginInvoke(new Action(() => messageHandled = true)); Wait(() => messageHandled); }
            if (phase == "connect") release.Set();
            Wait(() => cancelEntered.IsSet);
            var overlap = executor.ExecuteAsync(new DaxQueryRequest { QueryText = "overlap" }, CancellationToken.None);
            Assert(overlap.Result.State == DaxQueryExecutionState.Failed && !task.IsCompleted && session.Disposes == 0,
                "Admission/cleanup released while cancel was blocked.");
            release.Set(); releaseCancel.Set(); Wait(() => task.IsCompleted);
            Assert(task.Result.Reason == DaxQueryReason.UserCancellation && session.Disconnects == 1 && session.Disposes == 1, "Cancel cleanup/outcome wrong.");
            if (phase != "connect") Assert(session.Query == "captured", "Mutable request was read on worker.");
        }
        using (var release = new ManualResetEventSlim())
        using (var cancelEntered = new ManualResetEventSlim())
        using (var user = new CancellationTokenSource())
        {
            var session = new Session { OnExecute = () => Assert(release.Wait(10000), "Timeout release missing."), OnCancel = () => cancelEntered.Set() };
            var task = new PbiBenchAmoDaxQueryExecutor(() => session).ExecuteAsync(new DaxQueryRequest { QueryText = "timeout race", TimeoutSeconds = 1 }, user.Token);
            Wait(() => cancelEntered.IsSet); user.Cancel(); release.Set(); Wait(() => task.IsCompleted);
            Assert(task.Result.Reason == DaxQueryReason.UserCancellation, "User must win simultaneous timeout before terminal commit.");
        }
        using (var release = new ManualResetEventSlim())
        {
            var session = new Session { OnExecute = () => Assert(release.Wait(10000), "Timeout release missing."), OnCancel = () => release.Set() };
            var result = Run(session, new DaxQueryRequest { QueryText = "timeout", TimeoutSeconds = 1 });
            Assert(result.Reason == DaxQueryReason.Timeout && result.State == DaxQueryExecutionState.Cancelled, "Timeout category missing.");
        }
        foreach (var phase in new[] { "connect", "query" })
        {
            Action secret = () => throw new Exception("Password=sentinel; Bearer sentinel; PRIVATE DAX", new Exception("nested sentinel"));
            var session = new Session { OnDispose = () => throw new Exception("cleanup sentinel") };
            if (phase == "connect") session.OnConnect = secret; else session.OnExecute = secret;
            var result = Run(session);
            Assert(!result.ErrorMessage.Contains("sentinel") && result.State == DaxQueryExecutionState.Failed &&
                result.Reason == (phase == "connect" ? DaxQueryReason.Connection : DaxQueryReason.Query), "Unsafe diagnostics or cleanup masking.");
        }
        using (var user = new CancellationTokenSource())
        {
            var session = new Session();
            var task = new PbiBenchAmoDaxQueryExecutor(() => session).ExecuteAsync(new DaxQueryRequest { QueryText = "complete" }, user.Token);
            Wait(() => task.IsCompleted); user.Cancel();
            Assert(task.Result.State == DaxQueryExecutionState.Completed && session.Cancels == 0, "Late cancellation rewrote completion.");
        }
        using (var release = new ManualResetEventSlim())
        using (var entered = new ManualResetEventSlim())
        using (var user = new CancellationTokenSource())
        {
            var session = new Session { OnExecute = () => { entered.Set(); Assert(release.Wait(10000), "Cancel-throw release missing."); },
                OnCancel = () => { release.Set(); throw new Exception("cancel sentinel"); }, OnDispose = () => throw new Exception("dispose sentinel") };
            var task = new PbiBenchAmoDaxQueryExecutor(() => session).ExecuteAsync(new DaxQueryRequest { QueryText = "cancel throws" }, user.Token);
            Wait(() => entered.IsSet); user.Cancel(); Wait(() => task.IsCompleted);
            Assert(task.Result.Reason == DaxQueryReason.UserCancellation && session.Disposes == 1, "Cancel/cleanup error replaced original outcome.");
        }
        ProductionClose();
        Bounds();
        Console.WriteLine("T05/T06/T07: production orchestration, cancellation/UI message, ownership, diagnostics and retained bounds PASS");
    }
    private static void ProductionClose()
    {
        using (var entered = new ManualResetEventSlim())
        using (var releaseRead = new ManualResetEventSlim())
        using (var cancelEntered = new ManualResetEventSlim())
        using (var releaseCancel = new ManualResetEventSlim())
        {
            var session = new Session();
            session.Reader.OnRead = () => { entered.Set(); Assert(releaseRead.Wait(10000), "Close read release missing."); };
            session.OnCancel = () => { cancelEntered.Set(); Assert(releaseCancel.Wait(10000), "Close cancel release missing."); };
            using (var form = new PbiBenchDaxWorkbenchForm(new PbiBenchAmoDaxQueryExecutor(() => session), "controlled", ""))
            {
                var handle = form.Handle;
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                Action<string, object[]> invoke = (name, args) => form.GetType().GetMethod(name, flags).Invoke(form, args);
                Func<string, object> field = name => form.GetType().GetField(name, flags).GetValue(form);
                invoke("ExecuteQuery", new object[0]); Wait(() => entered.IsSet);
                var close = new FormClosingEventArgs(CloseReason.UserClosing, false);
                invoke("OnWorkbenchClosing", new object[] { form, close }); Wait(() => cancelEntered.IsSet);
                Assert(close.Cancel && field("_runCts") != null && !((Button)field("_executeButton")).Enabled, "Production close released the run/draft.");
                var draft = ((RichTextBox)field("_queryEditor")).Text;
                invoke("NewDocument", new object[0]);
                invoke("ExecuteQuery", new object[0]);
                Assert(((RichTextBox)field("_queryEditor")).Text == draft, "Document replacement during cleanup.");
                releaseRead.Set(); releaseCancel.Set(); Wait(() => field("_runCts") == null);
                var history = (DaxQueryHistory)field("_history");
                Assert(history.Count == 1 && history.Items[0].Reason == DaxQueryReason.UserCancellation &&
                    !((Button)field("_exportButton")).Enabled && !((RichTextBox)field("_queryEditor")).ReadOnly,
                    "Production close must publish one cancellation, clear export, and restore editor.");
            }
        }
    }
    private static void Bounds()
    {
        foreach (var n in new[] { 1, 2, 3 })
        {
            var result = Run(new Session(n), new DaxQueryRequest { QueryText = "rows", MaxRows = 2 });
            Assert(result.ReturnedRowCount == Math.Min(n, 2) && result.IsTruncated == (n > 2), "Row cap boundary.");
        }
        // Schema: 64 + 32 + 8 + 26 = 130. Row: 32 + 8 + 32 + 24 = 96.
        foreach (var budget in new[] { 225, 226, 227 })
        {
            var result = Run(new Session(), new DaxQueryRequest { QueryText = "budget", MaxRetainedBytes = budget });
            Assert(result.ReturnedRowCount == (budget >= 226 ? 1 : 0) && result.IsTruncated == (budget < 226), "Retained byte boundary.");
        }
        foreach (var columns in new[] { 256, 257 })
        {
            var result = Run(new Session(1, columns));
            Assert(result.State == (columns == 256 ? DaxQueryExecutionState.Completed : DaxQueryExecutionState.Failed), "Column limit.");
        }
        foreach (object value in new object[] { new string('界', 524288), new byte[1048576], new EvilValue() })
        {
            var session = new Session(); session.Reader.Value = value;
            var result = Run(session);
            Assert(result.IsTruncated && result.ReturnedRowCount == 0 && session.Reader.Values == 1, "Oversized/unknown cell retained or continued fetching.");
        }
        foreach (object value in new object[] { DBNull.Value, "界\r\n😀", new byte[] { 1, 2 } })
        {
            var session = new Session(); session.Reader.Value = value;
            var result = Run(session);
            Assert(!result.IsTruncated && result.ReturnedRowCount == 1, "Accepted value changed.");
            Assert(value == DBNull.Value ? result.Rows[0][0] == null : DaxResultRetention.FormatValue(value) == DaxResultRetention.FormatValue(result.Rows[0][0]), "Cell not exact.");
        }
        foreach (var size in new[] { DaxResultRetention.MaxCellBytes - 2, DaxResultRetention.MaxCellBytes, DaxResultRetention.MaxCellBytes + 2 })
        {
            var session = new Session(); session.Reader.Value = new string('界', (size - 24) / 2);
            var result = Run(session);
            Assert(result.IsTruncated == (size > DaxResultRetention.MaxCellBytes), "Exact string cell boundary.");
        }
        Assert(Run(new Session(0)).ReturnedRowCount == 0, "Empty rowset.");
    }
    private sealed class EvilValue { public override string ToString() => throw new Exception("Unknown ToString must never be invoked"); }
    internal sealed class Session : IDaxQuerySession
    {
        internal readonly Reader Reader;
        internal Action OnConnect, OnExecute, OnCancel, OnDispose;
        internal int Disconnects, Disposes, Cancels;
        internal string Query;
        internal Session(int rows = 1, int columns = 1) { Reader = new Reader(rows, columns); }
        public void Connect(string connectionString, int timeoutSeconds) => OnConnect?.Invoke();
        public IDataReader Execute(string query, string catalog) { Query = query; OnExecute?.Invoke(); return Reader; }
        public void Cancel() { Interlocked.Increment(ref Cancels); OnCancel?.Invoke(); }
        public void Disconnect() { Disconnects++; }
        public void Dispose() { Disposes++; OnDispose?.Invoke(); }
    }
    internal sealed class Reader : IDataReader
    {
        private readonly int _rows;
        private int _index;
        internal int Values;
        internal Action OnRead;
        internal object Value = 1;
        internal Reader(int rows, int columns) { _rows = rows; FieldCount = columns; }
        public int FieldCount { get; }
        public bool Read() { OnRead?.Invoke(); return _index++ < _rows; }
        public string GetName(int i) => "x"; // Duplicate names deliberately supported.
        public object GetValue(int i) { Values++; return Value; }
        public void Dispose() { }
        public void Close() { }
        public bool NextResult() => false;
        public DataTable GetSchemaTable() => null;
        public int Depth => 0;
        public bool IsClosed => false;
        public int RecordsAffected => 0;
        public object this[int i] => GetValue(i);
        public object this[string name] => GetValue(0);
        public bool GetBoolean(int i) => (bool)GetValue(i);
        public byte GetByte(int i) => (byte)GetValue(i);
        public long GetBytes(int i,long a,byte[] b,int c,int d) => throw new NotSupportedException();
        public char GetChar(int i) => (char)GetValue(i);
        public long GetChars(int i,long a,char[] b,int c,int d) => throw new NotSupportedException();
        public IDataReader GetData(int i) => throw new NotSupportedException();
        public string GetDataTypeName(int i) => "Object";
        public DateTime GetDateTime(int i) => (DateTime)GetValue(i);
        public decimal GetDecimal(int i) => (decimal)GetValue(i);
        public double GetDouble(int i) => (double)GetValue(i);
        public Type GetFieldType(int i) => typeof(object);
        public float GetFloat(int i) => (float)GetValue(i);
        public Guid GetGuid(int i) => (Guid)GetValue(i);
        public short GetInt16(int i) => (short)GetValue(i);
        public int GetInt32(int i) => (int)GetValue(i);
        public long GetInt64(int i) => (long)GetValue(i);
        public int GetOrdinal(string name) => 0;
        public string GetString(int i) => (string)GetValue(i);
        public int GetValues(object[] values) => throw new NotSupportedException();
        public bool IsDBNull(int i) => GetValue(i) == DBNull.Value;
    }
}
