using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly string _connectionString;
        private readonly string _databaseName;

        private PbiBenchAmoDaxQueryExecutor(string connectionString, string databaseName, string connectionLabel)
        {
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

        public async Task<DaxQueryResult> ExecuteAsync(DaxQueryRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrWhiteSpace(request.QueryText))
            {
                return new DaxQueryResult
                {
                    State = DaxQueryExecutionState.Failed,
                    ErrorMessage = "DAX query text is required."
                };
            }

            using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(request.TimeoutSeconds)))
            using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token))
            {
                return await Task.Run(
                    () => ExecuteCore(request, cancellationToken, timeoutCts.Token, linkedCts.Token),
                    CancellationToken.None).ConfigureAwait(false);
            }
        }

        private DaxQueryResult ExecuteCore(
            DaxQueryRequest request,
            CancellationToken userCancellationToken,
            CancellationToken timeoutToken,
            CancellationToken combinedToken)
        {
            var stopwatch = Stopwatch.StartNew();
            var server = new Server();

            try
            {
                combinedToken.ThrowIfCancellationRequested();
                server.Connect(_connectionString);
                combinedToken.ThrowIfCancellationRequested();

                using (combinedToken.Register(() => TryCancel(server)))
                {
                    var xmla = "<Statement>" + SecurityElement.Escape(request.QueryText.Trim()) + "</Statement>";
                    var properties = new Dictionary<string, string>
                    {
                        { "Catalog", _databaseName },
                        { "Format", "Tabular" }
                    };

                    XmlaResultCollection xmlaResults;
                    using (var reader = server.ExecuteReader(xmla, out xmlaResults, properties))
                    {
                        if (reader == null)
                        {
                            stopwatch.Stop();
                            return new DaxQueryResult
                            {
                                State = DaxQueryExecutionState.Failed,
                                DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                                ErrorMessage = "The Analysis Services endpoint did not return a tabular rowset."
                            };
                        }

                        var columns = Enumerable.Range(0, reader.FieldCount)
                            .Select(reader.GetName)
                            .ToArray();
                        var rows = new List<object[]>(Math.Min(request.MaxRows, 4096));

                        while (rows.Count < request.MaxRows && reader.Read())
                        {
                            combinedToken.ThrowIfCancellationRequested();
                            var row = new object[reader.FieldCount];
                            for (var i = 0; i < reader.FieldCount; i++)
                            {
                                var value = reader.GetValue(i);
                                row[i] = value == DBNull.Value ? null : value;
                            }
                            rows.Add(row);
                        }

                        var truncated = rows.Count == request.MaxRows && reader.Read();
                        combinedToken.ThrowIfCancellationRequested();
                        stopwatch.Stop();

                        return new DaxQueryResult
                        {
                            State = DaxQueryExecutionState.Completed,
                            DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                            Columns = columns,
                            Rows = rows.ToArray(),
                            IsTruncated = truncated
                        };
                    }
                }
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                return CancelledResult(stopwatch.ElapsedMilliseconds, request.TimeoutSeconds, userCancellationToken, timeoutToken);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                if (combinedToken.IsCancellationRequested)
                    return CancelledResult(stopwatch.ElapsedMilliseconds, request.TimeoutSeconds, userCancellationToken, timeoutToken);

                return new DaxQueryResult
                {
                    State = DaxQueryExecutionState.Failed,
                    DurationMilliseconds = stopwatch.ElapsedMilliseconds,
                    ErrorMessage = SafeErrorMessage(ex)
                };
            }
            finally
            {
                try
                {
                    if (server.Connected) server.Disconnect();
                }
                catch
                {
                    // Query cleanup must never mask the query result.
                }
            }
        }

        private static DaxQueryResult CancelledResult(
            long elapsedMilliseconds,
            int timeoutSeconds,
            CancellationToken userCancellationToken,
            CancellationToken timeoutToken)
        {
            var timedOut = timeoutToken.IsCancellationRequested && !userCancellationToken.IsCancellationRequested;
            return new DaxQueryResult
            {
                State = DaxQueryExecutionState.Cancelled,
                DurationMilliseconds = elapsedMilliseconds,
                ErrorMessage = timedOut
                    ? "Query timed out after " + timeoutSeconds + " seconds."
                    : "Query cancelled."
            };
        }

        private static void TryCancel(Server server)
        {
            try
            {
                if (server != null && server.Connected) server.CancelCommand();
            }
            catch
            {
                // Cancellation is best-effort at the transport boundary. The execution task will
                // still observe its linked cancellation token and return a Cancelled result.
            }
        }

        private static string SafeErrorMessage(Exception ex)
        {
            // Never concatenate or expose the cloned connection string. AMO exception messages
            // normally contain server-side query diagnostics; cap their length before surfacing.
            var message = ex?.Message;
            if (string.IsNullOrWhiteSpace(message)) return "DAX query execution failed.";
            message = message.Trim();
            return message.Length <= 2000 ? message : message.Substring(0, 2000) + "…";
        }
    }
}
