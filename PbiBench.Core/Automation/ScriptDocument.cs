using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PbiBench.Core.Automation
{
    /// <summary>
    /// One C# script tab in the script workspace (AUTO-001). Trust is per document and bound to the exact
    /// content hash that was confirmed: any edit revokes it, so the user re-confirms what will actually run.
    /// Trust is a consent record, not a security boundary: scripts are never sandboxed.
    /// </summary>
    public sealed class ScriptDocument
    {
        public const int MaxChars = 1024 * 1024;
        public const int MaxTitleChars = 128;

        /// <summary>Exact wording shown before the first run of each document version.</summary>
        public const string TrustStatement =
            "This C# script runs inside TE2 Enhanced with your Windows permissions. It is NOT sandboxed: " +
            "it can read and change files, start processes, use the network and change the loaded model. " +
            "Model changes are grouped in one TE2 undo step; anything outside the model cannot be undone. " +
            "Run only code you have reviewed and trust.";

        private string _text;
        private string _hash;
        private string? _savedHash;
        private string? _trustedHash;

        public ScriptDocument(string id, string title, string text, string? filePath = null, bool isSaved = false)
        {
            if (!IsValidId(id)) throw new ArgumentException("Script id must be 1-64 characters [A-Za-z0-9_-].", nameof(id));
            Id = id;
            Title = CleanTitle(title);
            _text = CheckText(text);
            _hash = Hash(_text);
            FilePath = filePath;
            _savedHash = isSaved ? _hash : null;
        }

        public static ScriptDocument CreateNew(string title, string text = "") => new ScriptDocument(Guid.NewGuid().ToString("N"), title, text);

        public string Id { get; }
        public string Title { get; private set; }
        public string Text => _text;
        public string? FilePath { get; private set; }
        /// <summary>Increments on every content change; used to discard compile results for an older version.</summary>
        public long Generation { get; private set; }
        public string ContentHash => _hash;
        public bool IsDirty => _savedHash == null ? _text.Length > 0 : _savedHash != _hash;
        public bool IsTrusted => _trustedHash != null && _trustedHash == _hash;

        public bool SetText(string text)
        {
            var checkedText = CheckText(text);
            if (checkedText == _text) return false;
            _text = checkedText;
            _hash = Hash(_text);
            Generation++;
            if (_trustedHash != _hash) _trustedHash = null;
            return true;
        }

        public void Rename(string title) => Title = CleanTitle(title);

        public void MarkSaved(string? filePath)
        {
            if (filePath != null) FilePath = filePath;
            _savedHash = _hash;
        }

        /// <summary>Records consent for exactly the content the user reviewed. Fails if the text changed since.</summary>
        public void ConfirmTrust(string reviewedHash)
        {
            if (!string.Equals(reviewedHash, _hash, StringComparison.Ordinal))
                throw new InvalidOperationException("The script changed after it was reviewed. Review it again before running.");
            _trustedHash = _hash;
        }

        public void RevokeTrust() => _trustedHash = null;

        public static bool IsValidId(string? id) =>
            !string.IsNullOrEmpty(id) && id!.Length <= 64 &&
            id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_');

        public static string Hash(string text)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(new UTF8Encoding(false).GetBytes(text ?? "")).Select(b => b.ToString("x2")));
        }

        private static string CheckText(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length > MaxChars) throw new ArgumentException("Script is limited to 1 MiB of text.");
            if (text.IndexOf('\0') >= 0) throw new ArgumentException("Script cannot contain NUL characters.");
            return text;
        }

        private static string CleanTitle(string? title)
        {
            var cleaned = new string((title ?? "").Where(c => !char.IsControl(c)).Take(MaxTitleChars).ToArray()).Trim();
            return cleaned.Length == 0 ? "Script" : cleaned;
        }
    }

    /// <summary>Bounded set of open script tabs. Closing a dirty tab requires the caller to pass force after asking the user.</summary>
    public sealed class ScriptWorkspace
    {
        public const int MaxDocuments = 20;
        private readonly List<ScriptDocument> _documents = new List<ScriptDocument>();

        public IReadOnlyList<ScriptDocument> Documents => _documents.AsReadOnly();

        public ScriptDocument New(string text = "")
        {
            if (_documents.Count >= MaxDocuments) throw new InvalidOperationException("At most 20 scripts can be open.");
            var ordinal = 1;
            while (_documents.Any(d => d.Title == "Script " + ordinal)) ordinal++;
            var doc = ScriptDocument.CreateNew("Script " + ordinal, text);
            _documents.Add(doc);
            return doc;
        }

        public ScriptDocument Add(ScriptDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (_documents.Count >= MaxDocuments) throw new InvalidOperationException("At most 20 scripts can be open.");
            if (_documents.Any(d => d.Id == document.Id)) throw new InvalidOperationException("Script is already open.");
            _documents.Add(document);
            return document;
        }

        public ScriptDocument? Find(string id) => _documents.FirstOrDefault(d => d.Id == id);

        /// <summary>Returns false (and keeps the tab) when it is dirty and force is false.</summary>
        public bool Close(string id, bool force)
        {
            var doc = Find(id);
            if (doc == null) return true;
            if (doc.IsDirty && !force) return false;
            _documents.Remove(doc);
            return true;
        }
    }
}
