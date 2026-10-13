using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Data;
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
            var connectionString = server.ConnectionString;
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

            var serverLabel = string.IsNullOrWhiteSpace(server.Name) ? "Analysis Services" : server.Name;
            executor = new PbiBenchAmoDaxQueryExecutor(
                connectionString,
                databaseName,
                serverLabel + " / " + databaseName);
            return true;
        }

        internal PbiBenchAmoDaxQueryExecutor(Func<IDaxQuerySession> factory)
        {
            _sessionFactory = factory;
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
            var finished = new ManualResetEventSlim();
            // Callback only enqueues work. Never runs blocking AMO on the cancelling/UI thread.
            System.Action signal = () =>
            {
                lock (sync)
                    if (connected && !finishing && cancelWork == null)
                        cancelWork = Task.Run(() => { do { try { session.Cancel(); } catch { } } while (!finished.Wait(100)); });
            };
            var registration = token.Register(signal);
            DaxQueryResult result;
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
                    result = reader == null ? Failure(DaxQueryReason.Query, "No tabular rowset was returned.")
                        : ReadResult(reader, request, token);
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
                try { session?.Disconnect(); } catch { }
                try { session?.Dispose(); } catch { }
            }
            // Terminal commit follows cleanup. User cancellation wins over timeout at this point.
            if (user.IsCancellationRequested || timeout.IsCancellationRequested)
                result = new DaxQueryResult { State = DaxQueryExecutionState.Cancelled,
                    Reason = user.IsCancellationRequested ? DaxQueryReason.UserCancellation : DaxQueryReason.Timeout,
                    ErrorMessage = user.IsCancellationRequested ? "Query cancelled." : "Query timeout requested; provider cleanup completed." };
            result.DurationMilliseconds = watch.ElapsedMilliseconds;
            return result;
        }

        private static DaxQueryResult ReadResult(IDataReader reader, DaxQueryRequest request, CancellationToken token)
        {
            var count = reader.FieldCount;
            if (count < 0 || count > DaxResultRetention.MaxColumns)
                return Failure(DaxQueryReason.ColumnLimit, "Result exceeds the 256-column limit.");
            var columns = new string[count];
            long retained = 64 + 32 + 8L * count;
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
        void Disconnect();
    }

    internal sealed class AmoDaxQuerySession : IDaxQuerySession
    {
        private readonly Server _server = new Server();
        public void Connect(string connectionString, int timeoutSeconds)
        {
            var builder = new System.Data.Common.DbConnectionStringBuilder { ConnectionString = connectionString };
            builder["Connect Timeout"] = timeoutSeconds;
            builder["Timeout"] = timeoutSeconds;
            _server.Connect(builder.ConnectionString);
        }
        public IDataReader Execute(string query, string catalog)
        {
            XmlaResultCollection results;
            return _server.ExecuteReader("<Statement>" + SecurityElement.Escape(query.Trim()) + "</Statement>",
                out results, new Dictionary<string, string> { { "Catalog", catalog }, { "Format", "Tabular" } });
        }
        public void Cancel() => _server.CancelCommand();
        public void Disconnect() { if (_server.Connected) _server.Disconnect(); }
        public void Dispose() => _server.Dispose();
    }
}
