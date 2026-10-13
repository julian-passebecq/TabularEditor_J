using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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

    /// <summary>
    /// The TE2 process that wrote a recovery copy: process id plus process start time (UTC). The start time tells a
    /// reused process id apart from the original writer. <see cref="Unknown"/> (pid 0) marks copies written before
    /// owners were recorded; they are treated as orphaned.
    /// </summary>
    public readonly struct RecoveryOwner : IEquatable<RecoveryOwner>
    {
        /// <summary>Start times read twice for the same process can differ by clock rounding; one second is far below any pid reuse gap.</summary>
        public static readonly TimeSpan StartTolerance = TimeSpan.FromSeconds(1);
        private static readonly Lazy<RecoveryOwner> CurrentLazy = new Lazy<RecoveryOwner>(ReadCurrent);

        public RecoveryOwner(int processId, DateTime startUtc)
        {
            if (processId < 0) throw new ArgumentOutOfRangeException(nameof(processId));
            ProcessId = processId;
            StartUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        }

        public int ProcessId { get; }
        public DateTime StartUtc { get; }
        public bool IsKnown => ProcessId > 0;
        public static RecoveryOwner Unknown => default;

        /// <summary>The current process (read once).</summary>
        public static RecoveryOwner Current => CurrentLazy.Value;

        public bool Equals(RecoveryOwner other) =>
            ProcessId == other.ProcessId && Math.Abs((StartUtc - other.StartUtc).Ticks) <= StartTolerance.Ticks;
        public override bool Equals(object? obj) => obj is RecoveryOwner other && Equals(other);
        public override int GetHashCode() => ProcessId;
        public override string ToString() =>
            IsKnown ? "pid " + ProcessId + " started " + StartUtc.ToString("u", System.Globalization.CultureInfo.InvariantCulture) : "unknown process";

        /// <summary>
        /// Default liveness probe: true when a process with this id runs and started at the recorded time. A process whose
        /// start time cannot be read (another user, elevated) counts as alive, so its copies are never offered by mistake.
        /// </summary>
        public static bool IsRunning(RecoveryOwner owner)
        {
            if (!owner.IsKnown) return false;
            if (owner.Equals(Current)) return true;
            try
            {
                using (var process = Process.GetProcessById(owner.ProcessId))
                {
                    DateTime start;
                    try
                    {
                        if (process.HasExited) return false;
                        start = process.StartTime.ToUniversalTime();
                    }
                    catch (InvalidOperationException) { return false; } // exited meanwhile
                    catch (System.ComponentModel.Win32Exception) { return true; }
                    catch (NotSupportedException) { return true; }
                    return Math.Abs((start - owner.StartUtc).Ticks) <= StartTolerance.Ticks;
                }
            }
            catch (ArgumentException) { return false; } // no process with this id
            catch (InvalidOperationException) { return false; }
        }

        private static RecoveryOwner ReadCurrent()
        {
            using (var process = Process.GetCurrentProcess())
                return new RecoveryOwner(process.Id, process.StartTime.ToUniversalTime());
        }
    }

    /// <summary>One recovery snapshot as listed from its manifest; <see cref="IsIntact"/> is false when the content hash no longer matches.</summary>
    public sealed class RecoveryEntry
    {
        internal RecoveryEntry(string documentId, string kind, string title, DateTime savedUtc, string sha256, int length, bool isIntact,
            RecoveryOwner owner, bool isOwnedByThisProcess, bool isRecoverable, bool isHeldByOtherInstance)
        {
            DocumentId = documentId; Kind = kind; Title = title; SavedUtc = savedUtc; Sha256 = sha256; Length = length; IsIntact = isIntact;
            Owner = owner; IsOwnedByThisProcess = isOwnedByThisProcess; IsRecoverable = isRecoverable; IsHeldByOtherInstance = isHeldByOtherInstance;
        }
        public string DocumentId { get; }
        public string Kind { get; }
        public string Title { get; }
        public DateTime SavedUtc { get; }
        public string Sha256 { get; }
        public int Length { get; }
        public bool IsIntact { get; }
        /// <summary>The process that wrote this copy (<see cref="RecoveryOwner.Unknown"/> for copies written before owners were recorded).</summary>
        public RecoveryOwner Owner { get; }
        /// <summary>Written by the store's owner (normally this process).</summary>
        public bool IsOwnedByThisProcess { get; }
        /// <summary>
        /// May be offered for restore: its writer process is gone (not running, or the pid now belongs to a process with
        /// another start time), or it was written by this process and no open editor holds it any more.
        /// </summary>
        public bool IsRecoverable { get; }
        /// <summary>Another running TE2 instance owns this copy (a live draft there); never offer, overwrite or delete it implicitly.</summary>
        public bool IsHeldByOtherInstance { get; }
    }

    /// <summary>
    /// Generic, opt-in local recovery store for unsaved text documents (C# scripts now, DAX documents later).
    /// Layout per snapshot version: <c>id.stamp.recovery</c> (content) plus <c>id.stamp.manifest.json</c> (hash manifest).
    /// Every write creates new files (temp write, then move to a name that does not exist yet) and only afterwards
    /// removes older versions of the same document, so no existing file is ever replaced in place (no File.Replace;
    /// OBS-001 is avoided by design, not by retries). Two TE2 instances never rewrite a shared index. Reads verify
    /// the SHA-256 recorded in the manifest. Retention prunes by age, then by count (oldest first). Nothing is sent
    /// anywhere; delete is explicit and only removes files this store owns.
    /// Multi-instance: every copy records its owner process (<see cref="RecoveryOwner"/>). Only copies whose owner is gone
    /// (or that an editor of this process released) are recoverable; restore moves a copy to a new id (<see cref="Adopt"/>),
    /// and Save / Don't save paths delete only this process's copies (<see cref="DeleteOwn"/>).
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
        // Ids held by open editors of this process, per root and owner. A copy written by this process is offered again
        // only after its editor released it (window closed or disposed); while held it is a live draft, not an orphan.
        private static readonly ConcurrentDictionary<string, byte> Held = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<RecoveryOwner, bool> _isOwnerAlive;

        public RecoveryStore(RecoveryStoreOptions options) : this(options, null, null) { }

        /// <summary>
        /// <paramref name="owner"/> defaults to <see cref="RecoveryOwner.Current"/> and <paramref name="isOwnerAlive"/> to
        /// <see cref="RecoveryOwner.IsRunning"/>; both are injectable so tests can model several TE2 instances.
        /// </summary>
        public RecoveryStore(RecoveryStoreOptions options, RecoveryOwner? owner, Func<RecoveryOwner, bool>? isOwnerAlive)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            Owner = owner ?? RecoveryOwner.Current;
            if (!Owner.IsKnown) throw new ArgumentException("A recovery store needs a known owner process.", nameof(owner));
            _isOwnerAlive = isOwnerAlive ?? RecoveryOwner.IsRunning;
        }

        public RecoveryStoreOptions Options { get; }

        /// <summary>The process recorded on every copy this store writes.</summary>
        public RecoveryOwner Owner { get; }

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
                    Title = title, SavedUtcTicks = nowUtc.Ticks, Sha256 = hash, Length = content.Length,
                    OwnerProcessId = Owner.ProcessId, OwnerStartUtcTicks = Owner.StartUtc.Ticks };
                Held[HeldKey(documentId)] = 0;
                // Content first, manifest second: a crash in between leaves orphan content (pruned later), never a manifest without content.
                WriteNew(ContentPath(documentId, stamp), bytes);
                using (var stream = new MemoryStream())
                {
                    Serializer.WriteObject(stream, manifest);
                    WriteNew(ManifestPath(documentId, stamp), stream.ToArray());
                }
                // Older versions are removed only after the new one is complete; a locked old file is left for the next prune.
                // Only this process's versions: another instance's copy under the same id is never removed implicitly.
                foreach (var old in VersionsLocked(documentId).Where(v => v.Stamp != stamp && OwnerOf(v).Equals(Owner))) TryDeleteVersion(documentId, old.Stamp!);
                PruneLocked(nowUtc);
                return new RecoveryEntry(documentId, kind, title, nowUtc, hash, content.Length, true, Owner, true, false, false);
            }
        }

        /// <summary>Lists the newest snapshot per document, newest first, verifying each content hash. Unreadable manifests are skipped.</summary>
        public IReadOnlyList<RecoveryEntry> List()
        {
            if (!Options.Enabled || !Directory.Exists(Options.Root)) return Array.Empty<RecoveryEntry>();
            lock (_gate)
            {
                return LatestLocked().Select(ToEntry).OrderByDescending(e => e.SavedUtc).ThenBy(e => e.DocumentId, StringComparer.Ordinal).ToArray();
            }
        }

        /// <summary>Copies that may be offered for restore (see <see cref="RecoveryEntry.IsRecoverable"/>); never another live instance's drafts.</summary>
        public IReadOnlyList<RecoveryEntry> ListRecoverable() => List().Where(e => e.IsRecoverable).ToArray();

        /// <summary>
        /// Restores a recoverable copy under a NEW id owned by this process: the new copy is written and verified first,
        /// and only then are the source versions removed. Throws <see cref="InvalidOperationException"/> when the source is
        /// held by a running instance (or an open editor of this process), <see cref="InvalidDataException"/> when it fails
        /// its hash check. If the source cannot be removed afterwards it stays on disk (offered again later), never lost.
        /// </summary>
        public RecoveryEntry Adopt(string sourceId, string newId, DateTime nowUtc, out string text)
        {
            if (!IsValidDocumentId(sourceId)) throw new ArgumentException("Invalid document id.", nameof(sourceId));
            if (!IsValidDocumentId(newId)) throw new ArgumentException("Invalid document id.", nameof(newId));
            if (string.Equals(sourceId, newId, StringComparison.Ordinal)) throw new ArgumentException("A restored copy needs a new id.", nameof(newId));
            if (!Options.Enabled) throw new InvalidOperationException("Recovery is disabled.");
            lock (_gate)
            {
                var source = Directory.Exists(Options.Root) ? Newest(VersionsLocked(sourceId)) : null;
                if (source == null || !TryReadVerified(source, out var verified)) throw new InvalidDataException("Recovery copy is missing or does not match its hash manifest.");
                if (!ToEntry(source).IsRecoverable) throw new InvalidOperationException("This recovery copy belongs to a TE2 instance that is still running.");
                var adopted = Save(newId, source.Kind ?? "", source.Title ?? "", verified!, nowUtc)!;
                var written = Newest(VersionsLocked(newId).Where(m => OwnerOf(m).Equals(Owner)));
                if (written == null || !TryReadVerified(written, out var check) || check != verified)
                    throw new InvalidDataException("The restored recovery copy could not be verified; the source copy was kept.");
                TryDeleteDocument(sourceId);
                text = verified!;
                return adopted;
            }
        }

        /// <summary>Marks an id as no longer held by an open editor of this process (window closed or disposed); its copy stays on disk.</summary>
        public void Release(string documentId)
        {
            if (IsValidDocumentId(documentId)) Held.TryRemove(HeldKey(documentId), out _);
        }

        /// <summary>
        /// Deletes the versions of one document written by this store's owner (Save / Don't save). Copies of other
        /// instances, even under the same id, are left alone. Releases the id. IO failures surface to the caller.
        /// </summary>
        public bool DeleteOwn(string documentId)
        {
            if (!IsValidDocumentId(documentId)) throw new ArgumentException("Invalid document id.", nameof(documentId));
            Release(documentId);
            if (!Directory.Exists(Options.Root)) return false;
            lock (_gate)
            {
                var any = false;
                foreach (var version in VersionsLocked(documentId).Where(v => OwnerOf(v).Equals(Owner)))
                {
                    // Manifest first so a half-deleted version is never listed with missing content.
                    File.Delete(ManifestPath(documentId, version.Stamp!));
                    var content = ContentPath(documentId, version.Stamp!);
                    if (File.Exists(content)) File.Delete(content);
                    any = true;
                }
                return any;
            }
        }

        private RecoveryEntry ToEntry(RecoveryManifest m)
        {
            var owner = OwnerOf(m);
            var mine = owner.IsKnown && owner.Equals(Owner);
            var otherAlive = !mine && owner.IsKnown && _isOwnerAlive(owner);
            var recoverable = mine ? !Held.ContainsKey(HeldKey(m.DocumentId!)) : !otherAlive;
            return new RecoveryEntry(m.DocumentId!, m.Kind ?? "", m.Title ?? "", new DateTime(m.SavedUtcTicks, DateTimeKind.Utc), m.Sha256 ?? "",
                m.Length, TryReadVerified(m, out _), owner, mine, recoverable, otherAlive);
        }

        private static RecoveryOwner OwnerOf(RecoveryManifest m) =>
            m.OwnerProcessId > 0 && m.OwnerStartUtcTicks > 0 && m.OwnerStartUtcTicks <= DateTime.MaxValue.Ticks
                ? new RecoveryOwner(m.OwnerProcessId, new DateTime(m.OwnerStartUtcTicks, DateTimeKind.Utc)) : RecoveryOwner.Unknown;

        private string HeldKey(string documentId) => Options.Root + "|" + Owner.ProcessId + "|" + Owner.StartUtc.Ticks + "|" + documentId;

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
            Release(documentId);
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
            // Added with multi-instance safety; manifests without them load as 0 (unknown owner = orphan, recoverable).
            [DataMember(Order = 8, IsRequired = false)] public int OwnerProcessId { get; set; }
            [DataMember(Order = 9, IsRequired = false)] public long OwnerStartUtcTicks { get; set; }
        }
    }
}