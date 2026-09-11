using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PbiBench.Core.Dax
{
    /// <summary>A single local DAX document. Nothing is persisted until Save is requested.</summary>
    public sealed class DaxDocument
    {
        public const int MaxFileBytes = 4 * 1024 * 1024;
        private string _savedText = string.Empty;
        private byte[]? _diskHash;

        public string? FilePath { get; private set; }
        public string Text { get; set; } = string.Empty;
        public bool IsDirty => Normalize(Text) != _savedText;

        public void New(string text = "", bool unsaved = false)
        {
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
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
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
