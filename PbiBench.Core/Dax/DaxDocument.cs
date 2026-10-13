using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using PbiBench.Core.Documents;
using PbiBench.Core.IO;

namespace PbiBench.Core.Dax
{
    /// <summary>What <see cref="DaxDocument.CheckDisk"/> found for a file-backed document.</summary>
    public enum DaxDiskState
    {
        /// <summary>Never opened from or saved to a file (or a deletion was acknowledged).</summary>
        NotOnDisk,
        Unchanged,
        Changed,
        Missing,
        /// <summary>The file exists but could not be read (locked, too large, access denied).</summary>
        Unreadable
    }

    /// <summary>A single local DAX document. Nothing is persisted until Save is requested.</summary>
    public sealed class DaxDocument : IWorkspaceDocument
    {
        public const int MaxFileBytes = 4 * 1024 * 1024;
        private const string ExternalBaseline = "\0external-change-acknowledged";
        private string _savedText = string.Empty;
        private byte[]? _diskHash;

        /// <summary>Stable per-instance identity (32 hex chars, valid as a RecoveryStore id); never reused.</summary>
        public string Id { get; } = Guid.NewGuid().ToString("N");
        /// <summary>Increments whenever the content is replaced by New/Open; edits and saves keep it.</summary>
        public long Generation { get; private set; }
        public string? FilePath { get; private set; }
        public string Text { get; set; } = string.Empty;
        public bool IsDirty => Normalize(Text) != _savedText;
        public string Title => FilePath == null ? "Untitled.dax" : Path.GetFileName(FilePath);

        public void New(string text = "", bool unsaved = false)
        {
            Generation++;
            FilePath = null;
            _diskHash = null;
            Text = text ?? string.Empty;
            _savedText = unsaved ? string.Empty : Normalize(Text);
        }

        public void Open(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var bytes = ReadBounded(fullPath);
            // Only UTF-8 (optionally with its BOM). StreamReader autodetection also accepts
            // UTF-16/32, which would violate the document encoding contract.
            var offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            var text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            if (text.IndexOf('\0') >= 0) throw new IOException("The selected file is not a text document.");

            // Commit document state only after reading and decoding succeeded.
            Generation++;
            FilePath = fullPath;
            Text = text;
            _savedText = Normalize(text);
            _diskHash = Hash(bytes);
        }

        public void Save(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var bytes = new UTF8Encoding(false, true).GetBytes(Text);
            if (bytes.Length > MaxFileBytes) throw new IOException("DAX documents are limited to 4 MiB.");
            var sameFile = string.Equals(fullPath, FilePath, StringComparison.OrdinalIgnoreCase);
            if (sameFile && _diskHash != null &&
                (!File.Exists(fullPath) || !Equal(Hash(ReadBounded(fullPath)), _diskHash)))
                throw new IOException("This file changed or was deleted outside the Workbench. Use Save As with a different name to preserve your edits.");

            var temporary = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(fullPath)) SafeFileReplace.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            FilePath = fullPath;
            _diskHash = Hash(bytes);
            _savedText = Normalize(Text);
        }

        /// <summary>
        /// Compares the file on disk with the bytes last opened or saved (SHA-256). Read-only: never
        /// changes the document. Unreadable files are reported, not assumed unchanged.
        /// </summary>
        public DaxDiskState CheckDisk()
        {
            if (FilePath == null || _diskHash == null) return DaxDiskState.NotOnDisk;
            try
            {
                if (!File.Exists(FilePath)) return DaxDiskState.Missing;
                return Equal(Hash(ReadBounded(FilePath)), _diskHash) ? DaxDiskState.Unchanged : DaxDiskState.Changed;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                return DaxDiskState.Unreadable;
            }
        }

        /// <summary>
        /// Explicit "keep my version": the current disk bytes become the save baseline, so the next Save
        /// to the same path may overwrite them (a deleted file may be recreated). The buffer is kept and
        /// marked dirty because it no longer matches the disk. Returns the state that was acknowledged;
        /// Unchanged/NotOnDisk/Unreadable change nothing.
        /// </summary>
        public DaxDiskState AcknowledgeExternalChange()
        {
            var state = CheckDisk();
            if (state == DaxDiskState.Changed)
            {
                try { _diskHash = Hash(ReadBounded(FilePath!)); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return DaxDiskState.Unreadable; }
            }
            else if (state == DaxDiskState.Missing) _diskHash = null;
            if (state == DaxDiskState.Changed || state == DaxDiskState.Missing) _savedText = ExternalBaseline;
            return state;
        }

        private static byte[] ReadBounded(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > MaxFileBytes) throw new IOException("DAX documents are limited to 4 MiB.");
                using (var buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
        }

        private static string Normalize(string text) => (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
        private static byte[] Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return hash.ComputeHash(bytes);
        }
        private static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }
}
