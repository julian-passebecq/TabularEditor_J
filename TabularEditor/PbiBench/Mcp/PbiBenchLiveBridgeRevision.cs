using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using PbiBench.Core.Snapshot;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Mcp
{
    /// <summary>UI-thread lifetime stamp, separate from the immutable disk snapshot protocol.</summary>
    internal sealed class PbiBenchLiveBridgeRevision : IDisposable
    {
        private TabularModelHandler _handler;
        private long _epoch;
        private string _modelId = "";
        private readonly int _thread = Thread.CurrentThread.ManagedThreadId;
        private string _lastSelection = "";
        private long _selectionEpoch;
        private bool _disposed;
        internal string ModelId => _modelId;
        internal void AssertUi()
        { if (Thread.CurrentThread.ManagedThreadId != _thread || _disposed) throw new InvalidOperationException("UI_THREAD_OR_DISPOSED"); }
        private void Edited(object sender, EventArgs e) { _epoch++; }
        internal void SelectionChanged(object sender, EventArgs e) { AssertUi(); _selectionEpoch++; }
        internal ModelSnapshotV1 Capture(TabularModelHandler handler, IEnumerable<ITabularNamedObject> selected, bool expressions, out string revision, out string selection, long contextGeneration = 0)
        {
            AssertUi();
            if (handler?.Model == null) throw new InvalidOperationException("NO_MODEL");
            if (!ReferenceEquals(handler, _handler))
            {
                if (_handler != null) _handler.UndoManager.UndoStateChanged -= Edited;
                _handler = handler; _modelId = Guid.NewGuid().ToString("N"); _epoch++;
                handler.UndoManager.UndoStateChanged += Edited;
            }
            var items = (selected ?? Enumerable.Empty<ITabularNamedObject>()).ToArray();
            // Include expressions for the internal fingerprint only. Serialization still enforces the credential heuristic.
            var stamp = PbiBenchModelSnapshotPublisher.Build(handler.Model, items, true, new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            stamp.PublishedAtUtc = "";
            var content = ModelSnapshotSerializer.Serialize(stamp);
            var selectionContent = string.Join("\n", items.Select(x => x.ObjectType + "|" + (x as ITabularTableObject)?.Table?.Name + "|" + x.Name));
            if (selectionContent != _lastSelection) { _lastSelection = selectionContent; _selectionEpoch++; }
            selection = Hash(_selectionEpoch.ToString(CultureInfo.InvariantCulture) + "|" + selectionContent);
            revision = Hash(_modelId + "|" + _epoch.ToString(CultureInfo.InvariantCulture) + "|" + contextGeneration.ToString(CultureInfo.InvariantCulture) + "|" + content);
            return PbiBenchModelSnapshotPublisher.Build(handler.Model, items, expressions, DateTime.UtcNow);
        }
        internal static string Hash(string value)
        { using (var sha = SHA256.Create()) return "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant(); }
        public void Dispose()
        { if (_disposed) return; AssertUi(); _disposed = true; if (_handler != null) _handler.UndoManager.UndoStateChanged -= Edited; _handler = null; }
    }
}
