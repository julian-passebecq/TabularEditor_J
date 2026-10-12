using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace PbiBench.Core.Documents
{
    /// <summary>
    /// Options for <see cref="RecoveryStore"/>. Recovery is opt-in: a store created with
    /// <see cref="Enabled"/> false never touches the disk (saves are ignored, lists are empty).
    /// </summary>
    public sealed class RecoveryStoreOptions
    {
        public const int MaxRetentionDays = 90;
        public const int MaxFileLimit = 500;
        public const int DefaultMaxContentChars = 2 * 1024 * 1024;

        public RecoveryStoreOptions(string root, bool enabled, int retentionDays = 7, int maxFiles = 50, int maxContentChars = DefaultMaxContentChars)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Recovery root is required.", nameof(root));
            if (retentionDays < 1 || retentionDays > MaxRetentionDays) throw new ArgumentOutOfRangeException(nameof(retentionDays));
            if (maxFiles < 1 || maxFiles > MaxFileLimit) throw new ArgumentOutOfRangeException(nameof(maxFiles));
            if (maxContentChars < 1 || maxContentChars > 16 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(maxContentChars));
            Root = Path.GetFullPath(root);
            Enabled = enabled;
            RetentionDays = retentionDays;
            MaxFiles = maxFiles;
            MaxContentChars = maxContentChars;
        }

        public string Root { get; }
        public bool Enabled { get; }
        public int RetentionDays { get; }
        public int MaxFiles { get; }
        public int MaxContentChars { get; }

        /// <summary>Per-user default: %LOCALAPPDATA%\TE2Enhanced\recovery (never inside a repository).</summary>
        public static string DefaultRoot =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TE2Enhanced", "recovery");
    }

    /// <summary>One recovery snapshot as listed from its manifest; <see cref="IsIntact"/> is false when the content hash no longer matches.</summary>
    public sealed class RecoveryEntry
    {
        internal RecoveryEntry(string documentId, string kind, string title, DateTime savedUtc, string sha256, int length, bool isIntact)
        { DocumentId = documentId; Kind = kind; Title = title; SavedUtc = savedUtc; Sha256 = sha256; Length = length; IsIntact = isIntact; }
        public string DocumentId { get; }
        public string Kind { get; }
        public string Title { get; }
        public DateTime SavedUtc { get; }
        public string Sha256 { get; }
        public int Length { get; }
        public bool IsIntact { get; }
    }

    /// <summary>
    /// Generic, opt-in local recovery store for unsaved text documents (C# scripts now, DAX documents later).
    /// Layout per snapshot version: <c>id.stamp.recovery</c> (content) plus <c>id.stamp.manifest.json</c> (hash manifest).
    /// Every write creates new files (temp write, then move to a name that does not exist yet) and only afterwards
    /// removes older versions of the same document, so no existing file is ever replaced in place (no File.Replace;
    /// OBS-001 is avoided by design, not by retries). Two TE2 instances never rewrite a shared index. Reads verify
    /// the SHA-256 recorded in the manifest. Retention prunes by age, then by count (oldest first). Nothing is sent
    /// anywhere; delete is explicit and only removes files this store owns.
    /// </summary>
    public sealed class RecoveryStore
    {
        public const string ContentExtension = ".recovery";
        public const string ManifestExtension = ".manifest.json";
        private const int MaxIdLength = 64;
        private const int MaxTitleLength = 256;
        private const int MaxKindLength = 32;
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(RecoveryManifest));
        private readonly object _gate = new object();

        public RecoveryStore(RecoveryStoreOptions options) { Options = options ?? throw new ArgumentNullException(nameof(options)); }

        public RecoveryStoreOptions Options { get; }

        /// <summary>Plain-language disclosure for the opt-in prompt and settings.</summary>
        public string Disclosure =>
            "Recovery copies of unsaved documents are kept in plain text at " + Options.Root +
            " for up to " + Options.RetentionDays + " days (at most " + Options.MaxFiles + " documents). " +
            "They stay on this computer, are never uploaded, and can be deleted at any time.";

        public static bool IsValidDocumentId(string? id) =>
            !string.IsNullOrEmpty(id) && id!.Length <= MaxIdLength &&
            id.All(c => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_');

        /// <summary>Saves a recovery copy. Returns null (and writes nothing) when the store is disabled.</summary>
        public RecoveryEntry? Save(string documentId, string kind, string title, string content, DateTime nowUtc)
        {
            if (!IsValidDocumentId(documentId)) throw new ArgumentException("Document id must be 1-64 characters [A-Za-z0-9_-].", nameof(documentId));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (content.Length > Options.MaxContentChars) throw new ArgumentException("Document is too large for recovery.", nameof(content));
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", nameof(nowUtc));
            if (!Options.Enabled) return null;
            kind = Clean(kind, MaxKindLength);
            title = Clean(title, MaxTitleLength);
            var bytes = new UTF8Encoding(false).GetBytes(content);
            var hash = Hash(bytes);
            lock (_gate)
            {
                Directory.CreateDirectory(Options.Root);
                var stamp = nowUtc.Ticks.ToString("x16") + "-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                var manifest = new RecoveryManifest { Version = RecoveryManifest.CurrentVersion, DocumentId = documentId, Stamp = stamp, Kind = kind,
                    Title = title, SavedUtcTicks = nowUtc.Ticks, Sha256 = hash, Length = content.Length };
                // Content first, manifest second: a crash in between leaves orphan content (pruned later), never a manifest without content.
                WriteNew(ContentPath(documentId, stamp), bytes);
                using (var stream = new MemoryStream())
                {
                    Serializer.WriteObject(stream, manifest);
                    WriteNew(ManifestPath(documentId, stamp), stream.ToArray());
                }
                // Older versions are removed only after the new one is complete; a locked old file is left for the next prune.
                foreach (var old in VersionsLocked(documentId).Where(v => v.Stamp != stamp)) TryDeleteVersion(documentId, old.Stamp!);
                PruneLocked(nowUtc);
                return new RecoveryEntry(documentId, kind, title, nowUtc, hash, content.Length, true);
            }
        }

        /// <summary>Lists the newest snapshot per document, newest first, verifying each content hash. Unreadable manifests are skipped.</summary>
        public IReadOnlyList<RecoveryEntry> List()
        {
            if (!Options.Enabled || !Directory.Exists(Options.Root)) return Array.Empty<RecoveryEntry>();
            lock (_gate)
            {
                return LatestLocked().Select(m => new RecoveryEntry(m.DocumentId!, m.Kind ?? "", m.Title ?? "",
                        new DateTime(m.SavedUtcTicks, DateTimeKind.Utc), m.Sha256 ?? "", m.Length, TryReadVerified(m, out _)))
                    .OrderByDescending(e => e.SavedUtc).ThenBy(e => e.DocumentId, StringComparer.Ordinal).ToArray();
            }
        }

        /// <summary>Returns the newest recovered text, or throws <see cref="InvalidDataException"/> when missing or the hash does not match.</summary>
        public string Read(string documentId)
        {
            if (!IsValidDocumentId(documentId)) throw new ArgumentException("Invalid document id.", nameof(documentId));
            if (!Options.Enabled) throw new InvalidOperationException("Recovery is disabled.");
            lock (_gate)
            {
                var manifest = Directory.Exists(Options.Root) ? Newest(VersionsLocked(documentId)) : null;
                if (manifest == null || !TryReadVerified(manifest, out var text)) throw new InvalidDataException("Recovery copy is missing or does not match its hash manifest.");
                return text!;
            }
        }

        /// <summary>Explicit delete of every version of one document. Works even when the store is disabled; IO failures surface to the caller.</summary>
        public bool Delete(string documentId)
        {
            if (!IsValidDocumentId(documentId)) throw new ArgumentException("Invalid document id.", nameof(documentId));
            if (!Directory.Exists(Options.Root)) return false;
            lock (_gate) return DeleteDocumentLocked(documentId);
        }

        /// <summary>Explicit delete of every file this store owns; unrelated files in the folder are left alone. Returns the number of documents removed.</summary>
        public int DeleteAll()
        {
            if (!Directory.Exists(Options.Root)) return 0;
            lock (_gate)
            {
                var ids = OwnedFiles().Select(f => f.Id).Distinct(StringComparer.Ordinal).ToArray();
                return ids.Count(DeleteDocumentLocked);
            }
        }

        /// <summary>Applies retention: removes snapshots older than RetentionDays, then the oldest documents beyond MaxFiles.</summary>
        public int Prune(DateTime nowUtc)
        {
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", nameof(nowUtc));
            if (!Directory.Exists(Options.Root)) return 0;
            lock (_gate) return PruneLocked(nowUtc);
        }

        private int PruneLocked(DateTime nowUtc)
        {
            var cutoff = nowUtc.AddDays(-Options.RetentionDays).Ticks;
            var latest = LatestLocked().OrderByDescending(m => m.SavedUtcTicks).ThenBy(m => m.DocumentId, StringComparer.Ordinal).ToList();
            var removed = 0;
            for (var i = 0; i < latest.Count; i++)
                if (latest[i].SavedUtcTicks < cutoff || i >= Options.MaxFiles)
                    if (TryDeleteDocument(latest[i].DocumentId!)) removed++;
            // Orphan content (manifest never written) is removed once its stamp is past retention.
            var manifests = new HashSet<string>(OwnedFiles().Where(f => f.IsManifest).Select(f => f.Id + "." + f.Stamp), StringComparer.Ordinal);
            foreach (var orphan in OwnedFiles().Where(f => !f.IsManifest && !manifests.Contains(f.Id + "." + f.Stamp) && StampTicks(f.Stamp) < cutoff))
                TryDelete(orphan.Path);
            return removed;
        }

        private IEnumerable<RecoveryManifest> LatestLocked() =>
            ManifestsLocked().GroupBy(m => m.DocumentId!, StringComparer.Ordinal).Select(g => Newest(g)!);

        private IEnumerable<RecoveryManifest> VersionsLocked(string documentId) =>
            ManifestsLocked().Where(m => m.DocumentId == documentId).ToArray();

        private static RecoveryManifest? Newest(IEnumerable<RecoveryManifest> versions) =>
            versions.OrderByDescending(m => m.SavedUtcTicks).ThenByDescending(m => m.Stamp, StringComparer.Ordinal).FirstOrDefault();

        private IEnumerable<RecoveryManifest> ManifestsLocked()
        {
            var list = new List<RecoveryManifest>();
            foreach (var file in OwnedFiles().Where(f => f.IsManifest))
            {
                var manifest = ReadManifest(file.Path);
                if (manifest != null && manifest.DocumentId == file.Id && manifest.Stamp == file.Stamp) list.Add(manifest);
            }
            return list;
        }

        private IEnumerable<OwnedFile> OwnedFiles()
        {
            if (!Directory.Exists(Options.Root)) yield break;
            foreach (var path in Directory.GetFiles(Options.Root))
            {
                var name = Path.GetFileName(path);
                var isManifest = name.EndsWith(ManifestExtension, StringComparison.OrdinalIgnoreCase);
                var isContent = !isManifest && name.EndsWith(ContentExtension, StringComparison.OrdinalIgnoreCase);
                if (!isManifest && !isContent) continue;
                var core = name.Substring(0, name.Length - (isManifest ? ManifestExtension.Length : ContentExtension.Length));
                var dot = core.IndexOf('.');
                if (dot <= 0) continue;
                var id = core.Substring(0, dot);
                var stamp = core.Substring(dot + 1);
                if (IsValidDocumentId(id) && IsValidStamp(stamp)) yield return new OwnedFile(path, id, stamp, isManifest);
            }
        }

        private static RecoveryManifest? ReadManifest(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 64 * 1024) return null;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    var manifest = Serializer.ReadObject(stream) as RecoveryManifest;
                    return manifest != null && manifest.Version == RecoveryManifest.CurrentVersion && IsValidDocumentId(manifest.DocumentId) &&
                        IsValidStamp(manifest.Stamp) ? manifest : null;
                }
            }
            catch (IOException) { return null; }
            catch (SerializationException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private bool TryReadVerified(RecoveryManifest manifest, out string? text)
        {
            text = null;
            try
            {
                var path = ContentPath(manifest.DocumentId!, manifest.Stamp!);
                if (!File.Exists(path) || new FileInfo(path).Length > (long)Options.MaxContentChars * 4) return false;
                var bytes = File.ReadAllBytes(path);
                if (!string.Equals(Hash(bytes), manifest.Sha256, StringComparison.Ordinal)) return false;
                text = new UTF8Encoding(false, true).GetString(bytes);
                return text.Length == manifest.Length;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (DecoderFallbackException) { return false; }
        }

        private bool DeleteDocumentLocked(string id)
        {
            var any = false;
            foreach (var file in OwnedFiles().Where(f => f.Id == id).OrderBy(f => f.IsManifest ? 0 : 1).ToArray())
            { File.Delete(file.Path); any = true; }
            return any;
        }

        private bool TryDeleteDocument(string id)
        {
            try { return DeleteDocumentLocked(id); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private void TryDeleteVersion(string id, string stamp)
        {
            // Manifest first so a half-deleted version is never listed with missing content.
            if (TryDelete(ManifestPath(id, stamp))) TryDelete(ContentPath(id, stamp));
        }

        private static bool TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static void WriteNew(string destination, byte[] bytes)
        {
            var temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                File.Move(temp, destination); // destination name is new (stamp contains a random suffix); fails rather than overwrite
            }
            finally { TryDelete(temp); }
        }

        private string ContentPath(string id, string stamp) => Path.Combine(Options.Root, id + "." + stamp + ContentExtension);
        private string ManifestPath(string id, string stamp) => Path.Combine(Options.Root, id + "." + stamp + ManifestExtension);
        private static bool IsValidStamp(string? stamp) =>
            stamp != null && stamp.Length == 29 && stamp[16] == '-' && stamp.All(c => c == '-' || (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        private static long StampTicks(string stamp) =>
            long.TryParse(stamp.Substring(0, 16), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : 0;
        private static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
        }
        private static string Clean(string? value, int max) => new string((value ?? "").Where(c => !char.IsControl(c)).Take(max).ToArray());

        private sealed class OwnedFile
        {
            public OwnedFile(string path, string id, string stamp, bool isManifest) { Path = path; Id = id; Stamp = stamp; IsManifest = isManifest; }
            public string Path { get; }
            public string Id { get; }
            public string Stamp { get; }
            public bool IsManifest { get; }
        }

        [DataContract]
        internal sealed class RecoveryManifest
        {
            public const int CurrentVersion = 1;
            [DataMember(Order = 0)] public int Version { get; set; }
            [DataMember(Order = 1)] public string? DocumentId { get; set; }
            [DataMember(Order = 2)] public string? Stamp { get; set; }
            [DataMember(Order = 3)] public string? Kind { get; set; }
            [DataMember(Order = 4)] public string? Title { get; set; }
            [DataMember(Order = 5)] public long SavedUtcTicks { get; set; }
            [DataMember(Order = 6)] public string? Sha256 { get; set; }
            [DataMember(Order = 7)] public int Length { get; set; }
        }
    }
}