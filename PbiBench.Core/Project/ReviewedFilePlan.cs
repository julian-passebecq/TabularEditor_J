using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;

namespace PbiBench.Core.Project
{
    public enum ReviewedFileChangeKind { Create, Replace, Delete }

    /// <summary>One reviewed file change. Create requires the target to be absent; Replace and Delete
    /// require the exact SHA-256 the reviewer saw (lowercase hex).</summary>
    public sealed class ReviewedFileChange
    {
        public ReviewedFileChange(string relativePath, ReviewedFileChangeKind kind, string? expectedBeforeHash, byte[]? newContent)
        {
            RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
            Kind = kind;
            ExpectedBeforeHash = expectedBeforeHash?.Trim().ToLowerInvariant();
            NewContent = newContent;
            if (kind == ReviewedFileChangeKind.Create && expectedBeforeHash != null) throw new ArgumentException("Create expects an absent file.");
            if (kind != ReviewedFileChangeKind.Create && string.IsNullOrEmpty(ExpectedBeforeHash)) throw new ArgumentException("Replace/Delete need the reviewed before-hash.");
            if (kind == ReviewedFileChangeKind.Delete && newContent != null) throw new ArgumentException("Delete has no content.");
            if (kind != ReviewedFileChangeKind.Delete && newContent == null) throw new ArgumentException("Create/Replace need content.");
        }
        public string RelativePath { get; }
        public ReviewedFileChangeKind Kind { get; }
        public string? ExpectedBeforeHash { get; }
        public byte[]? NewContent { get; }
        public string? AfterHash => NewContent == null ? null : ReviewedFilePlan.Hash(NewContent);
    }

    public sealed class ReviewedFilePreviewItem
    {
        public string RelativePath { get; internal set; } = "";
        public ReviewedFileChangeKind Kind { get; internal set; }
        public string? ExpectedBeforeHash { get; internal set; }
        public string? CurrentHash { get; internal set; }
        public string? AfterHash { get; internal set; }
        public long? CurrentLength { get; internal set; }
        public long? AfterLength { get; internal set; }
        public bool IsStale { get; internal set; }
        public string StaleReason { get; internal set; } = "";
    }

    public sealed class ReviewedFilePlanStaleException : InvalidOperationException
    {
        public ReviewedFilePlanStaleException(IReadOnlyList<ReviewedFilePreviewItem> stale)
            : base("The reviewed files changed on disk since review: " + string.Join(", ", stale.Select(s => s.RelativePath)) + ". Nothing was written.")
        { Stale = stale; }
        public IReadOnlyList<ReviewedFilePreviewItem> Stale { get; }
    }

    public sealed class ReviewedFilePlanApplyException : IOException
    {
        public ReviewedFilePlanApplyException(string message, bool rolledBack, string journalPath, Exception inner) : base(message, inner)
        { RolledBack = rolledBack; JournalPath = journalPath; }
        public bool RolledBack { get; }
        public string JournalPath { get; }
    }

    public sealed class ReviewedFilePlanResult
    {
        public string JournalPath { get; internal set; } = "";
        public int FilesWritten { get; internal set; }
        public string State { get; internal set; } = "";
    }

    /// <summary>Test/diagnostic hooks. Never used to retry: a failed apply rolls back once and reports.</summary>
    public sealed class ReviewedFilePlanOptions
    {
        /// <summary>Called before change N (0-based) is committed to its target.</summary>
        public Action<int, string>? BeforeCommit { get; set; }
        /// <summary>Called before restore step N (0-based) during an explicit journal restore.</summary>
        public Action<int, string>? BeforeRestore { get; set; }
    }

    public static class ReviewedFileJournalState
    {
        public const string Prepared = "Prepared";
        public const string Applying = "Applying";
        public const string Applied = "Applied";
        public const string RolledBack = "RolledBack";
        public const string RollbackIncomplete = "RollbackIncomplete";
        public const string Restored = "Restored";
        public const string RestoreIncomplete = "RestoreIncomplete";
    }

    /// <summary>Write-once journal (plan + backups). State lives in separate marker files so no file is
    /// rewritten in quick succession (Windows scanners can hold a just-written file).</summary>
    [DataContract]
    public sealed class ReviewedFileJournal
    {
        /// <summary>Derived from marker files when read; not serialized.</summary>
        public string State { get; internal set; } = ReviewedFileJournalState.Prepared;
        public const int CurrentVersion = 1;
        [DataMember(Name = "version", Order = 1)] public int Version { get; set; } = CurrentVersion;
        [DataMember(Name = "planId", Order = 2)] public string PlanId { get; set; } = "";
        [DataMember(Name = "label", Order = 3)] public string Label { get; set; } = "";
        [DataMember(Name = "root", Order = 4)] public string Root { get; set; } = "";
        [DataMember(Name = "createdUtc", Order = 5)] public string CreatedUtc { get; set; } = "";
        [DataMember(Name = "entries", Order = 8)] public ReviewedFileJournalEntry[] Entries { get; set; } = Array.Empty<ReviewedFileJournalEntry>();
    }

    [DataContract]
    public sealed class ReviewedFileJournalEntry
    {
        [DataMember(Name = "path", Order = 1)] public string RelativePath { get; set; } = "";
        [DataMember(Name = "kind", Order = 2)] public string Kind { get; set; } = "";
        [DataMember(Name = "beforeHash", Order = 3, EmitDefaultValue = false)] public string? BeforeHash { get; set; }
        [DataMember(Name = "afterHash", Order = 4, EmitDefaultValue = false)] public string? AfterHash { get; set; }
        [DataMember(Name = "backup", Order = 5, EmitDefaultValue = false)] public string? BackupFile { get; set; }
    }

    /// <summary>
    /// Generic reviewed multi-file write. Preview shows exact current hashes; Apply re-checks every
    /// before-hash (stale = nothing written), backs up originals into a journal folder outside the
    /// project, writes each target via a sibling temp file + replace, and on the first failure
    /// rolls every committed file back from the journal. Restore replays the journal later and
    /// refuses files changed since apply. No retries and no waits: IO failures surface once.
    /// </summary>
    public sealed class ReviewedFilePlan
    {
        public const int MaxFiles = 256;
        public const long MaxFileBytes = 16L * 1024 * 1024;
        private const string JournalFileName = "journal.json";
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(ReviewedFileJournal));
        private readonly List<ReviewedFileChange> _changes = new List<ReviewedFileChange>();

        public ReviewedFilePlan(string root, string label)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A root folder is required.", nameof(root));
            Root = NormalizeRoot(root);
            Label = (label ?? "").Length > 200 ? label!.Substring(0, 200) : (label ?? "");
            PlanId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        public string Root { get; }
        public string Label { get; }
        public string PlanId { get; }
        public IReadOnlyList<ReviewedFileChange> Changes => _changes;

        public void Add(ReviewedFileChange change)
        {
            if (change == null) throw new ArgumentNullException(nameof(change));
            if (_changes.Count == MaxFiles) throw new InvalidOperationException("A reviewed plan holds at most " + MaxFiles + " files.");
            if (change.NewContent != null && change.NewContent.LongLength > MaxFileBytes) throw new ArgumentException("File exceeds the reviewed write limit.");
            var full = ResolveTarget(Root, change.RelativePath);
            if (_changes.Any(c => string.Equals(ResolveTarget(Root, c.RelativePath), full, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("The same file appears twice in the plan.");
            _changes.Add(change);
        }

        /// <summary>Convenience: Replace when the file exists now (hash read now), otherwise Create.</summary>
        public ReviewedFileChange AddFromCurrent(string relativePath, byte[] newContent)
        {
            var full = ResolveTarget(Root, relativePath);
            var change = File.Exists(full)
                ? new ReviewedFileChange(relativePath, ReviewedFileChangeKind.Replace, HashFile(full), newContent)
                : new ReviewedFileChange(relativePath, ReviewedFileChangeKind.Create, null, newContent);
            Add(change);
            return change;
        }

        public IReadOnlyList<ReviewedFilePreviewItem> Preview()
        {
            var result = new List<ReviewedFilePreviewItem>();
            foreach (var change in _changes)
            {
                var full = ResolveTarget(Root, change.RelativePath);
                var exists = File.Exists(full);
                var item = new ReviewedFilePreviewItem {
                    RelativePath = change.RelativePath, Kind = change.Kind, ExpectedBeforeHash = change.ExpectedBeforeHash,
                    CurrentHash = exists ? HashFile(full) : null, CurrentLength = exists ? new FileInfo(full).Length : (long?)null,
                    AfterHash = change.AfterHash, AfterLength = change.NewContent?.LongLength
                };
                if (Directory.Exists(full)) { item.IsStale = true; item.StaleReason = "A folder now occupies this path."; }
                else if (change.Kind == ReviewedFileChangeKind.Create && exists) { item.IsStale = true; item.StaleReason = "File was created since review."; }
                else if (change.Kind != ReviewedFileChangeKind.Create && !exists) { item.IsStale = true; item.StaleReason = "File was removed since review."; }
                else if (change.Kind != ReviewedFileChangeKind.Create && !string.Equals(item.CurrentHash, change.ExpectedBeforeHash, StringComparison.Ordinal))
                { item.IsStale = true; item.StaleReason = "File content changed since review."; }
                result.Add(item);
            }
            return result;
        }

        public ReviewedFilePlanResult Apply(string journalRoot, ReviewedFilePlanOptions? options = null)
        {
            if (_changes.Count == 0) throw new InvalidOperationException("The plan is empty.");
            var journalDirectory = PrepareJournalDirectory(journalRoot, Root, PlanId);
            var stale = Preview().Where(p => p.IsStale).ToArray();
            if (stale.Length > 0) throw new ReviewedFilePlanStaleException(stale);

            var journal = new ReviewedFileJournal {
                PlanId = PlanId, Label = Label, Root = Root, CreatedUtc = DateTime.UtcNow.ToString("o"),
                Entries = _changes.Select((c, i) => new ReviewedFileJournalEntry {
                    RelativePath = c.RelativePath, Kind = c.Kind.ToString(), BeforeHash = c.ExpectedBeforeHash, AfterHash = c.AfterHash,
                    BackupFile = c.Kind == ReviewedFileChangeKind.Create ? null : "backup-" + i.ToString("000") + ".bin"
                }).ToArray()
            };
            Directory.CreateDirectory(journalDirectory);
            var journalPath = Path.Combine(journalDirectory, JournalFileName);
            // Backups first: any failure here leaves targets untouched.
            for (var i = 0; i < _changes.Count; i++)
            {
                var entry = journal.Entries[i];
                if (entry.BackupFile == null) continue;
                var source = ResolveTarget(Root, entry.RelativePath);
                var bytes = File.ReadAllBytes(source);
                if (Hash(bytes) != entry.BeforeHash) throw new ReviewedFilePlanStaleException(Preview().Where(p => p.IsStale).ToArray());
                WriteNew(Path.Combine(journalDirectory, entry.BackupFile), bytes);
            }
            WriteNew(journalPath, SerializeJournal(journal));

            var index = 0;
            try
            {
                for (; index < _changes.Count; index++)
                {
                    var change = _changes[index];
                    var target = ResolveTarget(Root, change.RelativePath);
                    options?.BeforeCommit?.Invoke(index, change.RelativePath);
                    if (change.Kind == ReviewedFileChangeKind.Delete) File.Delete(target);
                    else CommitContent(target, change.NewContent!);
                }
            }
            catch (Exception error)
            {
                var rolledBack = TryRollBack(journal, journalDirectory, index);
                TryMark(journalDirectory, rolledBack ? ReviewedFileJournalState.RolledBack : ReviewedFileJournalState.RollbackIncomplete);
                throw new ReviewedFilePlanApplyException(
                    rolledBack ? "Writing '" + _changes[index].RelativePath + "' failed; all files were restored from the journal."
                               : "Writing failed and some files could not be restored; recover them from the journal at " + journalDirectory + ".",
                    rolledBack, journalPath, error);
            }
            Mark(journalDirectory, ReviewedFileJournalState.Applied);
            return new ReviewedFilePlanResult { JournalPath = journalPath, FilesWritten = _changes.Count, State = ReviewedFileJournalState.Applied };
        }

        /// <summary>Lists journals newest first (bounded), for an explicit restore UI.</summary>
        public static IReadOnlyList<string> ListJournals(string journalRoot, int max = 50)
        {
            if (!Directory.Exists(journalRoot)) return Array.Empty<string>();
            return Directory.EnumerateDirectories(journalRoot)
                .Select(d => Path.Combine(d, JournalFileName)).Where(File.Exists)
                .OrderByDescending(p => Path.GetFileName(Path.GetDirectoryName(p)), StringComparer.Ordinal)
                .Take(Math.Max(1, Math.Min(max, 500))).ToArray();
        }

        public static ReviewedFileJournal ReadJournal(string journalPath)
        {
            var info = new FileInfo(journalPath);
            if (!info.Exists || info.Length > 4 * 1024 * 1024) throw new InvalidDataException("Journal missing or too large.");
            using (var stream = File.OpenRead(journalPath))
            {
                var journal = Serializer.ReadObject(stream) as ReviewedFileJournal ?? throw new InvalidDataException("Journal could not be read.");
                if (journal.Version != ReviewedFileJournal.CurrentVersion) throw new InvalidDataException("Unsupported journal version.");
                if (journal.Entries == null || journal.Entries.Length > MaxFiles) throw new InvalidDataException("Journal entries are invalid.");
                journal.State = ReadState(Path.GetDirectoryName(Path.GetFullPath(journalPath))!);
                return journal;
            }
        }

        /// <summary>Restore preview: which files still match what the plan wrote (restorable) and which changed since.</summary>
        public static IReadOnlyList<ReviewedFilePreviewItem> PreviewRestore(string journalPath)
        {
            var journal = ReadJournal(journalPath);
            var root = NormalizeRoot(journal.Root);
            var result = new List<ReviewedFilePreviewItem>();
            for (var i = 0; i < journal.Entries.Length; i++)
            {
                var entry = journal.Entries[i];
                var full = ResolveTarget(root, entry.RelativePath);
                var current = File.Exists(full) ? HashFile(full) : null;
                var expected = entry.Kind == nameof(ReviewedFileChangeKind.Delete) ? null : entry.AfterHash;
                var item = new ReviewedFilePreviewItem { RelativePath = entry.RelativePath, CurrentHash = current, ExpectedBeforeHash = expected, AfterHash = entry.BeforeHash,
                    Kind = (ReviewedFileChangeKind)Enum.Parse(typeof(ReviewedFileChangeKind), entry.Kind) };
                // Restorable when the file still holds what the plan wrote, or already holds the original.
                if (!string.Equals(current, expected, StringComparison.Ordinal) && !string.Equals(current, entry.BeforeHash, StringComparison.Ordinal))
                { item.IsStale = true; item.StaleReason = "Changed since the plan was applied."; }
                result.Add(item);
            }
            return result;
        }

        /// <summary>Restores every file of an applied (or interrupted) journal. Refuses (writes nothing) when any
        /// file changed after apply, so a later edit is never silently overwritten.</summary>
        public static int Restore(string journalPath, ReviewedFilePlanOptions? options = null)
        {
            var journal = ReadJournal(journalPath);
            if (journal.State == ReviewedFileJournalState.Restored || journal.State == ReviewedFileJournalState.RolledBack)
                throw new InvalidOperationException("This journal was already restored (state: " + journal.State + ").");
            var stale = PreviewRestore(journalPath).Where(p => p.IsStale).ToArray();
            if (stale.Length > 0) throw new ReviewedFilePlanStaleException(stale);
            var directory = Path.GetDirectoryName(Path.GetFullPath(journalPath))!;
            var root = NormalizeRoot(journal.Root);
            var count = 0;
            try
            {
                for (var i = journal.Entries.Length - 1; i >= 0; i--)
                {
                    options?.BeforeRestore?.Invoke(count, journal.Entries[i].RelativePath);
                    RestoreEntry(root, directory, journal.Entries[i]);
                    count++;
                }
            }
            catch (Exception)
            {
                TryMark(directory, ReviewedFileJournalState.RestoreIncomplete);
                throw;
            }
            Mark(directory, ReviewedFileJournalState.Restored);
            return count;
        }

        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create()) return ToHex(sha.ComputeHash(bytes));
        }

        public static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                return ToHex(sha.ComputeHash(stream));
        }

        internal static string ToHex(byte[] bytes)
        {
            var text = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) text.Append(b.ToString("x2"));
            return text.ToString();
        }

        public static string NormalizeRoot(string root)
        {
            var full = Path.GetFullPath(root);
            var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 || trimmed.EndsWith(":", StringComparison.Ordinal) ? full : trimmed;
        }

        /// <summary>Resolves a plan-relative path, rejecting rooted paths, traversal and device-like names.</summary>
        public static string ResolveTarget(string root, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) throw new ArgumentException("A relative path is required.");
            if (Path.IsPathRooted(relativePath) || relativePath.IndexOf(':') >= 0) throw new ArgumentException("Plan paths must be relative to the project root.");
            if (relativePath.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || relativePath.Any(char.IsControl)) throw new ArgumentException("Plan path has invalid characters.");
            var segments = relativePath.Split('/', '\\');
            if (segments.Any(s => s.Length == 0 || s == "." || s == ".." || s.EndsWith(".", StringComparison.Ordinal) || s.EndsWith(" ", StringComparison.Ordinal)))
                throw new ArgumentException("Plan path segments must be plain names.");
            var full = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Plan path escapes the project root.");
            return full;
        }

        private static string PrepareJournalDirectory(string journalRoot, string projectRoot, string planId)
        {
            if (string.IsNullOrWhiteSpace(journalRoot)) throw new ArgumentException("A journal folder is required.");
            var journalFull = NormalizeRoot(journalRoot);
            if (string.Equals(journalFull, projectRoot, StringComparison.OrdinalIgnoreCase) ||
                journalFull.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Journals live outside the project folder so they are never committed with it.");
            return Path.Combine(journalFull, planId);
        }

        private static void CommitContent(string target, byte[] content)
        {
            var directory = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory, "." + Path.GetFileName(target) + ".te2tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                WriteNew(temp, content);
                if (File.Exists(target)) File.Replace(temp, target, null, true);
                else File.Move(temp, target);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static void WriteNew(string path, byte[] content)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(content, 0, content.Length); stream.Flush(true); }
        }

        private bool TryRollBack(ReviewedFileJournal journal, string journalDirectory, int failedIndex)
        {
            var ok = true;
            // The failing index may have partly committed (e.g. replace succeeded before an error); include it.
            for (var i = Math.Min(failedIndex, journal.Entries.Length - 1); i >= 0; i--)
            {
                try { RestoreEntry(Root, journalDirectory, journal.Entries[i]); }
                catch (Exception) { ok = false; }
            }
            return ok;
        }

        private static void RestoreEntry(string root, string journalDirectory, ReviewedFileJournalEntry entry)
        {
            var target = ResolveTarget(root, entry.RelativePath);
            if (entry.BackupFile == null)
            {
                // Created by the plan: remove only if it is still what the plan wrote.
                if (File.Exists(target) && HashFile(target) == entry.AfterHash) File.Delete(target);
                return;
            }
            if (File.Exists(target) && HashFile(target) == entry.BeforeHash) return;
            var backup = Path.Combine(journalDirectory, Path.GetFileName(entry.BackupFile));
            var bytes = File.ReadAllBytes(backup);
            if (Hash(bytes) != entry.BeforeHash) throw new InvalidDataException("Journal backup is corrupt for " + entry.RelativePath + ".");
            CommitContent(target, bytes);
        }

        private static readonly string[] StateOrder = {
            ReviewedFileJournalState.Restored, ReviewedFileJournalState.RestoreIncomplete, ReviewedFileJournalState.RolledBack,
            ReviewedFileJournalState.RollbackIncomplete, ReviewedFileJournalState.Applied };

        private static string ReadState(string directory)
        {
            foreach (var state in StateOrder)
                if (File.Exists(Path.Combine(directory, state.ToLowerInvariant() + ".marker"))) return state;
            return ReviewedFileJournalState.Applying;
        }

        private static void Mark(string directory, string state)
        {
            var path = Path.Combine(directory, state.ToLowerInvariant() + ".marker");
            if (!File.Exists(path)) WriteNew(path, Encoding.UTF8.GetBytes(DateTime.UtcNow.ToString("o")));
        }

        private static void TryMark(string directory, string state) { try { Mark(directory, state); } catch (Exception) { } }

        private static byte[] SerializeJournal(ReviewedFileJournal journal)
        {
            using (var buffer = new MemoryStream()) { Serializer.WriteObject(buffer, journal); return buffer.ToArray(); }
        }
    }
}
