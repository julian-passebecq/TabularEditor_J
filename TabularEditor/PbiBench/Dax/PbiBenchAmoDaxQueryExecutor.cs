using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AnalysisServices;
using Server = Microsoft.AnalysisServices.Tabular.Server;
using PbiBench.Core.Dax;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Dax
{
    /// <summary>
    /// Executes read-only DAX query text through a dedicated AMO session using the same Analysis
    /// Services client libraries already shipped by TE2. The existing model connection is never
    /// reused for the command itself, so query cancellation cannot cancel an editor operation.
    /// </summary>
    internal sealed class PbiBenchAmoDaxQueryExecutor : IDaxQueryExecutor
    {
        private readonly Func<IDaxQuerySession> _sessionFactory;
        private int _active;
        private readonly string _connectionString;
        private readonly string _databaseName;
        private readonly int _abortAfterMilliseconds = DefaultAbortAfterMilliseconds;
        private readonly int _serverStopWaitMilliseconds = DefaultServerStopWaitMilliseconds;

        /// <summary>How long a cancel or timeout waits for the engine before the query session is ended.</summary>
        internal const int DefaultAbortAfterMilliseconds = 2000;

        /// <summary>
        /// After a cancel or timeout, how long the run waits for the server to show that the query has stopped
        /// before it reports StillRunning. Admission stays held meanwhile, so no new query overlaps it.
        /// </summary>
        internal const int DefaultServerStopWaitMilliseconds = 30000;

        private PbiBenchAmoDaxQueryExecutor(string connectionString, string databaseName, string connectionLabel)
        {
            _sessionFactory = () => new AmoDaxQuerySession();
            _connectionString = connectionString;
            _databaseName = databaseName;
            ConnectionLabel = connectionLabel;
        }

        public string ConnectionLabel { get; }

        public static bool TryCreate(
            TabularModelHandler handler,
            out PbiBenchAmoDaxQueryExecutor executor,
            out string unavailableReason)
        {
            executor = null;
            unavailableReason = string.Empty;

            if (handler == null || handler.Model == null)
            {
                unavailableReason = "No semantic model is loaded.";
                return false;
            }

            if (!handler.IsConnected || handler.Database?.Server == null || !handler.Database.Server.Connected)
            {
                unavailableReason = "Query execution requires a live Analysis Services / Power BI Desktop connection.";
                return false;
            }

            var server = handler.Database.Server;
            var connectionString = BuildQueryConnectionString(server.ConnectionString, handler.OpenedConnectionString);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                unavailableReason = "The live model connection cannot be cloned safely for query execution.";
                return false;
            }

            var databaseName = handler.Database.Name;
            if (string.IsNullOrWhiteSpace(databaseName))
            {
                unavailableReason = "The connected model does not expose an Analysis Services catalog name.";
                return false;
            }

            executor = new PbiBenchAmoDaxQueryExecutor(
                connectionString,
                databaseName,
                BuildConnectionLabel(server.Name, databaseName));
            return true;
        }

        private static readonly string[] PasswordKeys = { "Password", "PWD" };

        /// <summary>
        /// Connection string for the owned query session. AMO's Server.ConnectionString omits the password, so a
        /// service principal on powerbi:// XMLA (User ID=app:...;Password=...) could not sign in again from it
        /// (live gate, 2026-10-15: every query failed in 1 ms). The password is taken back from the string the
        /// editor connected with; every other key stays as AMO reports it. The result may hold a secret: it is
        /// only passed to AMO, never shown or logged.
        /// </summary>
        internal static string BuildQueryConnectionString(string serverConnectionString, string openedConnectionString)
        {
            if (string.IsNullOrWhiteSpace(serverConnectionString) || string.IsNullOrWhiteSpace(openedConnectionString)
                || openedConnectionString.IndexOf('=') < 0)
                return serverConnectionString;
            DbConnectionStringBuilder opened, clone;
            try
            {
                opened = new DbConnectionStringBuilder { ConnectionString = openedConnectionString };
                clone = new DbConnectionStringBuilder { ConnectionString = serverConnectionString };
            }
            catch (ArgumentException) { return serverConnectionString; }
            if (PasswordKeys.Any(key => clone.ContainsKey(key) && !string.IsNullOrEmpty(Convert.ToString(clone[key]))))
                return serverConnectionString;
            var source = PasswordKeys.FirstOrDefault(key => opened.ContainsKey(key) && !string.IsNullOrEmpty(Convert.ToString(opened[key])));
            if (source == null) return serverConnectionString;
            foreach (var key in PasswordKeys) clone.Remove(key);
            clone["Password"] = opened[source];
            return clone.ConnectionString;
        }

        /// <summary>Display label: server name and catalog only, never connection-string keys.</summary>
        internal static string BuildConnectionLabel(string serverName, string databaseName) =>
            (string.IsNullOrWhiteSpace(serverName) || serverName.IndexOf('=') >= 0 ? "Analysis Services" : serverName) + " / " + databaseName;

        internal PbiBenchAmoDaxQueryExecutor(Func<IDaxQuerySession> factory, int abortAfterMilliseconds = DefaultAbortAfterMilliseconds,
            int serverStopWaitMilliseconds = DefaultServerStopWaitMilliseconds)
        {
            _sessionFactory = factory;
            _abortAfterMilliseconds = abortAfterMilliseconds;
            _serverStopWaitMilliseconds = serverStopWaitMilliseconds;
            _connectionString = "";
            _databaseName = "";
            ConnectionLabel = "Controlled session";
        }

        public async Task<DaxQueryResult> ExecuteAsync(DaxQueryRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            // Snapshot before dispatch: caller mutations must not affect this run.
            var captured = new DaxQueryRequest { QueryText = request.QueryText, MaxRows = request.MaxRows,
                TimeoutSeconds = request.TimeoutSeconds, MaxRetainedBytes = request.MaxRetainedBytes };
            if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
                return Failure(DaxQueryReason.Unexpected, "Query cleanup is still active.");
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(captured.TimeoutSeconds)))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
                    return await Task.Run(() => ExecuteCore(captured, cancellationToken, timeout.Token, linked.Token)).ConfigureAwait(false);
            }
            finally { Volatile.Write(ref _active, 0); }
        }

        private DaxQueryResult ExecuteCore(DaxQueryRequest request, CancellationToken user, CancellationToken timeout, CancellationToken token)
        {
            var watch = Stopwatch.StartNew();
            IDaxQuerySession session = null;
            IDataReader reader = null;
            var sync = new object();
            var connected = false;
            var finishing = false;
            Task cancelWork = null;
            long cancelAt = -1;
            var finished = new ManualResetEventSlim();
            // Callback only enqueues work. Never runs blocking AMO on the cancelling/UI thread.
            // Cancel first asks the engine to stop the command. When the command is still running after
            // _abortAfterMilliseconds, the query session is ended once (Abort): the blocked call then returns
            // at once. Measured live (SSAS 2022, 2026-10-15): a formula-engine-bound query ignored the command
            // cancel for 16-56 s, while ending the session returned control in about 20-200 ms.
            System.Action signal = () =>
            {
                lock (sync)
                    if (connected && !finishing && cancelWork == null)
                    {
                        cancelAt = watch.ElapsedMilliseconds;
                        cancelWork = Task.Run(() =>
                        {
                            var since = Stopwatch.StartNew();
                            var aborted = false;
                            do
                            {
                                try
                                {
                                    if (!aborted && since.ElapsedMilliseconds >= _abortAfterMilliseconds) { aborted = true; session.Abort(); }
                                    else session.Cancel();
                                }
                                catch { }
                            } while (!finished.Wait(100));
                        });
                    }
            };
            var registration = token.Register(signal);
            DaxQueryResult result;
            var serverStop = DaxServerStopState.NotApplicable;
            long serverStopMilliseconds = 0;
            var phase = DaxQueryReason.Connection;
            try
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(request.QueryText))
                    result = Failure(DaxQueryReason.Query, "DAX query text is required.");
                else
                {
                    session = _sessionFactory();
                    session.Connect(_connectionString, request.TimeoutSeconds);
                    lock (sync) connected = true;
                    if (token.IsCancellationRequested) signal();
                    token.ThrowIfCancellationRequested();
                    phase = DaxQueryReason.Query;
                    reader = session.Execute(request.QueryText, _databaseName);
                    token.ThrowIfCancellationRequested();
                    result = reader == null ? new DaxQueryResult() : ReadResults(reader, request, token);
                }
            }
            catch (Exception ex)
            {
                // No provider message/body (including nested exceptions) is a diagnostic channel.
                var reason = ex is UnauthorizedAccessException ? DaxQueryReason.Authentication :
                    ex is TimeoutException ? DaxQueryReason.Timeout : phase;
                if (ex is ConnectionException connection)
                {
                    if (connection.ExceptionCause == ConnectionExceptionCause.AuthenticationFailed) reason = DaxQueryReason.Authentication;
                    if (connection.ExceptionCause == ConnectionExceptionCause.Timeout) reason = DaxQueryReason.Timeout;
                }
                result = Failure(reason, reason == DaxQueryReason.Connection ? "Could not connect to the query endpoint." :
                    reason == DaxQueryReason.Authentication ? "Query authentication failed." :
                    reason == DaxQueryReason.Timeout ? "The query provider reported a timeout." : "The endpoint could not execute the query.");
            }
            finally
            {
                lock (sync) finishing = true;
                finished.Set();
                registration.Dispose();
                // One owner retains admission until cancel, reader and session cleanup all finish.
                // A stuck provider remains active: no abandoned calls or hard deadline claim.
                if (cancelWork != null) cancelWork.GetAwaiter().GetResult();
                finished.Dispose();
                try { reader?.Dispose(); } catch { }
                // A released client call does not mean the engine stopped (live, SSAS 2022: formula-engine work
                // ran to completion after the session was ended). Before admission is released, the server is
                // asked whether it still runs this session's command, while the session id is still valid.
                if (cancelWork != null)
                {
                    try { serverStop = session.WaitForServerStop(_serverStopWaitMilliseconds); }
                    catch { serverStop = DaxServerStopState.Unknown; }
                    serverStopMilliseconds = Math.Max(0, watch.ElapsedMilliseconds - cancelAt);
                }
                try { session?.Disconnect(); } catch { }
                try { session?.Dispose(); } catch { }
            }
            // Terminal commit follows cleanup. User cancellation wins over timeout at this point.
            if (user.IsCancellationRequested || timeout.IsCancellationRequested)
                result = new DaxQueryResult { State = DaxQueryExecutionState.Cancelled,
                    Reason = user.IsCancellationRequested ? DaxQueryReason.UserCancellation : DaxQueryReason.Timeout,
                    ErrorMessage = CancelMessage(user.IsCancellationRequested, serverStop, serverStopMilliseconds) };
            result.ServerStop = serverStop;
            result.ServerStopMilliseconds = serverStopMilliseconds;
            result.DurationMilliseconds = watch.ElapsedMilliseconds;
            return result;
        }

        internal static string CancelMessage(bool user, DaxServerStopState stop, long milliseconds)
        {
            var head = user ? "Query cancelled" : "Query timeout";
            switch (stop)
            {
                case DaxServerStopState.Confirmed:
                    return head + "; the server stopped the query " + milliseconds + " ms after the cancel.";
                case DaxServerStopState.StillRunning:
                    return head + " on the client; the server was still computing the query " + (milliseconds / 1000) +
                        " s after the cancel. Formula-engine work can run to its end; no result from it is shown.";
                case DaxServerStopState.Unknown:
                    return head + " on the client; this endpoint did not let the server stop be checked.";
                default:
                    return user ? "Query cancelled." : "Query timeout requested; provider cleanup completed.";
            }
        }

        private static DaxQueryResult ReadResults(IDataReader reader, DaxQueryRequest request, CancellationToken token)
        {
            var sets = new List<DaxResultSet>();
            long retained = 0;
            var result = new DaxQueryResult();
            var visited = 0;
            do
            {
                token.ThrowIfCancellationRequested();
                // Bound even providers returning an arbitrary number of empty non-tabular sets.
                if (++visited > DaxQueryResult.MaxResultSets)
                { result.RemainingResultsNotRead = true; result.Reason = DaxQueryReason.ResultSetLimit; break; }
                if (reader.FieldCount == 0) continue;
                var set = ReadResult(reader, request, token, ref retained);
                if (set.State == DaxQueryExecutionState.Failed)
                {
                    if (sets.Count == 0) return set;
                    result.RemainingResultsNotRead = true; result.Reason = set.Reason; break;
                }
                sets.Add(new DaxResultSet { Columns = set.Columns, Rows = set.Rows, IsTruncated = set.IsTruncated, Reason = set.Reason });
                if (set.IsTruncated && result.Reason == DaxQueryReason.None) result.Reason = set.Reason;
                if (set.Reason == DaxQueryReason.MemoryLimit)
                { result.RemainingResultsNotRead = true; result.Reason = set.Reason; break; }
                token.ThrowIfCancellationRequested();
            } while (reader.NextResult());
            token.ThrowIfCancellationRequested();
            result.ResultSets = sets.ToArray();
            if (sets.Count > 0) { result.Columns = sets[0].Columns; result.Rows = sets[0].Rows; }
            result.IsTruncated = result.RemainingResultsNotRead || sets.Any(x => x.IsTruncated);
            return result;
        }

        private static DaxQueryResult ReadResult(IDataReader reader, DaxQueryRequest request, CancellationToken token, ref long retained)
        {
            var count = reader.FieldCount;
            if (count < 0 || count > DaxResultRetention.MaxColumns)
                return Failure(DaxQueryReason.ColumnLimit, "Result exceeds the 256-column limit.");
            var columns = new string[count];
            var schemaBytes = 64 + 32 + 8L * count;
            if (retained + schemaBytes > request.MaxRetainedBytes)
                return Failure(DaxQueryReason.MemoryLimit, "Result schema exceeds the shared retained-result budget.");
            retained += schemaBytes;
            for (var i = 0; i < count; i++)
            {
                var name = reader.GetName(i) ?? "";
                DaxResultRetention.TryEstimate(name, out var size);
                if (size > DaxResultRetention.MaxCellBytes || retained + size > request.MaxRetainedBytes)
                    return Failure(DaxQueryReason.MemoryLimit, "Result schema exceeds the retained-result budget.");
                columns[i] = name;
                retained += size;
            }
            var rows = new List<object[]>();
            var reason = DaxQueryReason.None;
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                if (rows.Count == request.MaxRows) { reason = DaxQueryReason.RowLimit; break; }
                // Account for row array and list/final-array references conservatively.
                long rowBytes = 32 + 8L * count + 32;
                if (retained + rowBytes > request.MaxRetainedBytes) { reason = DaxQueryReason.MemoryLimit; break; }
                var row = new object[count];
                for (var i = 0; i < count; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var value = reader.GetValue(i);
                    if (value == DBNull.Value) value = null;
                    if (!DaxResultRetention.TryEstimate(value, out var bytes)) { reason = DaxQueryReason.UnsupportedValue; break; }
                    if (bytes > DaxResultRetention.MaxCellBytes) { reason = DaxQueryReason.CellLimit; break; }
                    if (retained + rowBytes + bytes > request.MaxRetainedBytes) { reason = DaxQueryReason.MemoryLimit; break; }
                    rowBytes += bytes;
                    row[i] = value is byte[] binary ? (byte[])binary.Clone() : value;
                }
                if (reason != DaxQueryReason.None) break;
                retained += rowBytes;
                rows.Add(row);
            }
            token.ThrowIfCancellationRequested();
            return new DaxQueryResult { State = DaxQueryExecutionState.Completed, Columns = columns,
                Rows = rows.ToArray(), IsTruncated = reason != DaxQueryReason.None, Reason = reason };
        }

        private static DaxQueryResult Failure(DaxQueryReason reason, string message) =>
            new DaxQueryResult { State = DaxQueryExecutionState.Failed, Reason = reason, ErrorMessage = message };
    }

    internal interface IDaxQuerySession : IDisposable
    {
        void Connect(string connectionString, int timeoutSeconds);
        IDataReader Execute(string query, string catalog);
        void Cancel();
        /// <summary>Ends this query session on the server so a blocked Execute/Read returns. Called at most once.</summary>
        void Abort();
        /// <summary>
        /// Called once after a cancel or timeout, once the blocked call has returned and before Disconnect: waits
        /// up to <paramref name="timeoutMilliseconds"/> for the server to show that this session runs no command.
        /// </summary>
        DaxServerStopState WaitForServerStop(int timeoutMilliseconds);
        void Disconnect();
    }

    /// <summary>The endpoint reported a query error; the provider text is deliberately not carried.</summary>
    internal sealed class DaxProviderQueryException : Exception
    {
        internal DaxProviderQueryException() : base("The endpoint reported a query error.") { }
    }

    internal sealed class AmoDaxQuerySession : IDaxQuerySession
    {
        private readonly Server _server = new Server();
        private string _connectionString;
        private string _sessionId;
        public void Connect(string connectionString, int timeoutSeconds)
        {
            var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
            builder["Connect Timeout"] = timeoutSeconds;
            builder["Timeout"] = timeoutSeconds;
            _server.Connect(builder.ConnectionString);
            // Captured on the connecting thread: Abort runs on the cancel worker and must not touch _server.
            _connectionString = connectionString;
            _sessionId = _server.SessionID;
        }
        public IDataReader Execute(string query, string catalog)
        {
            XmlaResultCollection results;
            var reader = _server.ExecuteReader("<Statement>" + SecurityElement.Escape(query.Trim()) + "</Statement>",
                out results, new Dictionary<string, string> { { "Catalog", catalog }, { "Format", "Tabular" } });
            // A DAX error comes back in the XMLA result messages with no reader, not as an exception (seen live on
            // SSAS 2022, DAX-006 gate): without this check a failed query looked like an empty, completed one.
            if (HasError(results)) { try { reader?.Dispose(); } catch { } throw new DaxProviderQueryException(); }
            return reader;
        }
        internal static bool HasError(XmlaResultCollection results)
        {
            if (results == null) return false;
            foreach (XmlaResult result in results)
                foreach (XmlaMessage message in result.Messages)
                    if (message is XmlaError) return true;
            return false;
        }
        /// <summary>
        /// First step of a cancel. Live gates (SSAS 2022, 2026-10-13 and 2026-10-15): a formula-engine-bound query
        /// honours this only at its next check point (16-56 s on a loaded PC), so the executor escalates to Abort
        /// after DefaultAbortAfterMilliseconds. See handover/2026-10-15/DESKTOP_GATES.md.
        /// </summary>
        public void Cancel() => _server.CancelCommand();

        /// <summary>
        /// Ends the dedicated query session from a second, short-lived connection with an XMLA Cancel on its
        /// SessionID and CancelAssociated=true. The query connection drops, so the blocked ExecuteReader returns
        /// at once (live gate, 2026-10-15). The editor connection is a different session and is not touched.
        /// The engine may still finish the formula-engine step it is in before it notices; that work is not
        /// waited for and no result from it is ever shown.
        /// </summary>
        public void Abort()
        {
            var sessionId = _sessionId;
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(_connectionString)) return;
            Guid parsed;
            // Only a well-formed id ever reaches the XMLA text (no markup can be injected through it).
            if (!Guid.TryParseExact(sessionId.Trim(), "D", out parsed)) return;
            var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = _connectionString };
            builder["Connect Timeout"] = 10;
            builder["Timeout"] = 10;
            using (var control = new Server())
            {
                control.Connect(builder.ConnectionString);
                try
                {
                    control.Execute("<Cancel xmlns=\"http://schemas.microsoft.com/analysisservices/2003/engine\"><SessionID>" +
                        sessionId.Trim() + "</SessionID><CancelAssociated>true</CancelAssociated></Cancel>");
                }
                finally { if (control.Connected) control.Disconnect(); }
            }
        }

        /// <summary>
        /// Polls DISCOVER_SESSIONS and DISCOVER_COMMANDS from a second, short-lived connection: the query has
        /// stopped when its session is gone or the session's command has an end time (live, SSAS 2022: the end time
        /// appears when the engine really finishes, not when the client is released). Read-only. A DMV the
        /// endpoint refuses gives Unknown, never a guess.
        /// </summary>
        public DaxServerStopState WaitForServerStop(int timeoutMilliseconds)
        {
            var sessionId = _sessionId;
            Guid parsed;
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(_connectionString) || !Guid.TryParseExact(sessionId.Trim(), "D", out parsed))
                return DaxServerStopState.Unknown;
            var watch = Stopwatch.StartNew();
            var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = _connectionString };
            builder["Connect Timeout"] = 10;
            builder["Timeout"] = 10;
            using (var control = new Server())
            {
                try { control.Connect(builder.ConnectionString); }
                catch { return DaxServerStopState.Unknown; }
                try
                {
                    Guid self;
                    if (!Guid.TryParse(control.SessionID, out self)) return DaxServerStopState.Unknown;
                    while (true)
                    {
                        bool? running;
                        try { running = CommandRunning(control, parsed, self); }
                        catch { return DaxServerStopState.Unknown; }
                        if (running == null) return DaxServerStopState.Unknown;
                        if (running == false) return DaxServerStopState.Confirmed;
                        if (watch.ElapsedMilliseconds >= timeoutMilliseconds) return DaxServerStopState.StillRunning;
                        Thread.Sleep(200);
                    }
                }
                finally { if (control.Connected) control.Disconnect(); }
            }
        }

        /// <summary>
        /// True while the session's command has no end time; false once it has one or the session is gone. A missing
        /// session counts only when the DMV lists the control session itself, which runs as the same identity (a
        /// non-admin sees only their own sessions); otherwise the endpoint gives no proof and the answer is null.
        /// </summary>
        private static bool? CommandRunning(Server control, Guid sessionId, Guid controlSessionId)
        {
            var sessions = Rows(control, "SELECT SESSION_ID, SESSION_SPID FROM $SYSTEM.DISCOVER_SESSIONS");
            if (sessions == null) return null;
            Func<object[], Guid, bool> isSession = (row, wanted) => Guid.TryParse(Convert.ToString(row[0]), out var id) && id == wanted;
            var mine = sessions.FirstOrDefault(row => isSession(row, sessionId));
            if (mine == null) return sessions.Any(row => isSession(row, controlSessionId)) ? (bool?)false : null;
            var spid = Convert.ToString(mine[1]);
            var commands = Rows(control, "SELECT SESSION_SPID, COMMAND_END_TIME FROM $SYSTEM.DISCOVER_COMMANDS");
            if (commands == null) return null;
            var command = commands.FirstOrDefault(row => Convert.ToString(row[0]) == spid);
            return command != null && (command[1] == null || command[1] is DBNull);
        }

        private static List<object[]> Rows(Server control, string dmv)
        {
            XmlaResultCollection results;
            var rows = new List<object[]>();
            using (var reader = control.ExecuteReader("<Statement>" + SecurityElement.Escape(dmv) + "</Statement>", out results,
                new Dictionary<string, string> { { "Format", "Tabular" } }))
            {
                if (HasError(results) || reader == null) return null;
                while (reader.Read())
                {
                    var row = new object[reader.FieldCount];
                    reader.GetValues(row);
                    rows.Add(row);
                }
            }
            return rows;
        }

        public void Disconnect() { if (_server.Connected) _server.Disconnect(); }
        public void Dispose() => _server.Dispose();
    }
}
