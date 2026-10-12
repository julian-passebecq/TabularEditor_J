using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PbiBench.Core.Documents
{
    /// <summary>Minimal identity a workspace tab needs: stable id, optional file path and dirty state.</summary>
    public interface IWorkspaceDocument
    {
        string Id { get; }
        string? FilePath { get; }
        bool IsDirty { get; }
    }

    /// <summary>
    /// Ordered, bounded set of open documents with one active document (DAX-013). Pure bookkeeping:
    /// it never prompts, reads or writes files. Ids are unique; a file path can be open only once
    /// (case-insensitive full path) so two tabs never race to save the same file.
    /// </summary>
    public sealed class DocumentSet<T> where T : class, IWorkspaceDocument
    {
        public const int DefaultMaxDocuments = 32;
        private readonly List<T> _documents = new List<T>();
        private int _active = -1;

        public DocumentSet(int maxDocuments = DefaultMaxDocuments)
        {
            if (maxDocuments < 1 || maxDocuments > 256) throw new ArgumentOutOfRangeException(nameof(maxDocuments));
            MaxDocuments = maxDocuments;
        }

        public int MaxDocuments { get; }
        public IReadOnlyList<T> Documents => _documents;
        public int Count => _documents.Count;
        public bool CanAdd => _documents.Count < MaxDocuments;
        public T? Active => _active >= 0 && _active < _documents.Count ? _documents[_active] : null;
        public int ActiveIndex => _active;
        /// <summary>Dirty documents in tab order (what Close all / Exit must ask about).</summary>
        public IReadOnlyList<T> Dirty => _documents.Where(d => d.IsDirty).ToArray();

        /// <summary>Adds a document; throws when the set is full, the id is taken or the file is already open.</summary>
        public T Add(T document, bool activate = true)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (!CanAdd) throw new InvalidOperationException("At most " + MaxDocuments + " documents can be open. Close a tab first.");
            if (FindById(document.Id) != null) throw new InvalidOperationException("A document with this id is already open.");
            if (document.FilePath != null && FindByPath(document.FilePath) != null)
                throw new InvalidOperationException("This file is already open in another tab.");
            _documents.Add(document);
            if (activate || _active < 0) _active = _documents.Count - 1;
            return document;
        }

        public T? FindById(string? id) => id == null ? null : _documents.FirstOrDefault(d => string.Equals(d.Id, id, StringComparison.Ordinal));

        /// <summary>Finds an open document by file path (full path, case-insensitive). Invalid paths match nothing.</summary>
        public T? FindByPath(string? path, T? except = null)
        {
            var full = TryFullPath(path);
            if (full == null) return null;
            return _documents.FirstOrDefault(d => !ReferenceEquals(d, except) && d.FilePath != null &&
                string.Equals(TryFullPath(d.FilePath), full, StringComparison.OrdinalIgnoreCase));
        }

        public bool Activate(string id)
        {
            var index = _documents.FindIndex(d => string.Equals(d.Id, id, StringComparison.Ordinal));
            if (index < 0) return false;
            _active = index;
            return true;
        }

        /// <summary>Removes a document. The active document moves to the right neighbour, else the left one.</summary>
        public bool Remove(string id)
        {
            var index = _documents.FindIndex(d => string.Equals(d.Id, id, StringComparison.Ordinal));
            if (index < 0) return false;
            _documents.RemoveAt(index);
            if (_documents.Count == 0) _active = -1;
            else if (index < _active || _active >= _documents.Count) _active = Math.Max(0, _active - 1);
            return true;
        }

        /// <summary>Returns the document <paramref name="offset"/> tabs away from the active one, wrapping around.</summary>
        public T? Neighbour(int offset)
        {
            if (_documents.Count == 0) return null;
            var start = _active < 0 ? 0 : _active;
            var index = ((start + offset) % _documents.Count + _documents.Count) % _documents.Count;
            return _documents[index];
        }

        /// <summary>
        /// Close order for "close all": the active document first (the one the user is looking at), then
        /// the others left to right, so prompts follow what is visible and Cancel stops at a predictable tab.
        /// </summary>
        public IReadOnlyList<T> CloseAllOrder()
        {
            var active = Active;
            var order = new List<T>();
            if (active != null) order.Add(active);
            order.AddRange(_documents.Where(d => !ReferenceEquals(d, active)));
            return order;
        }

        private static string? TryFullPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
            { return null; }
        }
    }
}
