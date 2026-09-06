using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PbiBench.Core.Dax
{
    public enum DaxQueryExecutionState
    {
        Draft = 0,
        Running = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }

    /// <summary>
    /// Provider-neutral, bounded DAX query request. Execution is intentionally abstracted so
    /// the TE2 host can later target a Desktop/XMLA connection without coupling Core to ADOMD.
    /// </summary>
    public sealed class DaxQueryRequest
    {
        public const int DefaultMaxRows = 5000;
        public const int MaxAllowedRows = 100000;
        public const int DefaultTimeoutSeconds = 120;

        private int _maxRows = DefaultMaxRows;
        private int _timeoutSeconds = DefaultTimeoutSeconds;

        public string QueryText { get; set; } = string.Empty;

        public int MaxRows
        {
            get => _maxRows;
            set
            {
                if (value <= 0 || value > MaxAllowedRows)
                    throw new ArgumentOutOfRangeException(nameof(value), "MaxRows must be between 1 and " + MaxAllowedRows + ".");
                _maxRows = value;
            }
        }

        public int TimeoutSeconds
        {
            get => _timeoutSeconds;
            set
            {
                if (value <= 0 || value > 3600)
                    throw new ArgumentOutOfRangeException(nameof(value), "TimeoutSeconds must be between 1 and 3600.");
                _timeoutSeconds = value;
            }
        }
    }

    public sealed class DaxQueryResult
    {
        private string[] _columns = Array.Empty<string>();
        private object[][] _rows = Array.Empty<object[]>();

        public DaxQueryExecutionState State { get; set; } = DaxQueryExecutionState.Completed;
        public long DurationMilliseconds { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public string[] Columns
        {
            get => _columns;
            set => _columns = value ?? Array.Empty<string>();
        }

        public object[][] Rows
        {
            get => _rows;
            set => _rows = value ?? Array.Empty<object[]>();
        }
    }

    /// <summary>
    /// Host boundary for future query execution. Cancellation is part of the contract from day
    /// one so a concrete Desktop/XMLA adapter cannot accidentally expose an uncancellable API.
    /// </summary>
    public interface IDaxQueryExecutor
    {
        Task<DaxQueryResult> ExecuteAsync(DaxQueryRequest request, CancellationToken cancellationToken);
    }

    public sealed class DaxQueryHistoryEntry
    {
        public string QueryText { get; set; } = string.Empty;
        public DateTime ExecutedUtc { get; set; }
        public bool Succeeded { get; set; }
        public long DurationMilliseconds { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    /// <summary>
    /// In-memory bounded query history. Persistence is deliberately left to the host so Core
    /// does not silently write query text to disk. Consecutive identical queries collapse to
    /// the newest entry.
    /// </summary>
    public sealed class DaxQueryHistory
    {
        private readonly LinkedList<DaxQueryHistoryEntry> _items = new LinkedList<DaxQueryHistoryEntry>();

        public DaxQueryHistory(int capacity = 100)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            Capacity = capacity;
        }

        public int Capacity { get; }
        public int Count => _items.Count;
        public IReadOnlyList<DaxQueryHistoryEntry> Items => _items.ToArray();

        public DaxQueryHistoryEntry Add(
            string queryText,
            bool succeeded,
            long durationMilliseconds,
            string errorMessage = "",
            DateTime? executedUtc = null)
        {
            if (string.IsNullOrWhiteSpace(queryText))
                throw new ArgumentException("Query text is required.", nameof(queryText));
            if (durationMilliseconds < 0)
                throw new ArgumentOutOfRangeException(nameof(durationMilliseconds));

            var normalized = queryText.Trim();
            if (_items.First != null
                && string.Equals(_items.First.Value.QueryText, normalized, StringComparison.Ordinal))
            {
                _items.RemoveFirst();
            }

            var entry = new DaxQueryHistoryEntry
            {
                QueryText = normalized,
                ExecutedUtc = executedUtc ?? DateTime.UtcNow,
                Succeeded = succeeded,
                DurationMilliseconds = durationMilliseconds,
                ErrorMessage = errorMessage ?? string.Empty
            };

            _items.AddFirst(entry);
            while (_items.Count > Capacity) _items.RemoveLast();
            return entry;
        }

        public void Clear() => _items.Clear();
    }
}
