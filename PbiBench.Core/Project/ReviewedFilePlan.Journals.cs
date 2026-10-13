using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PbiBench.Core.Project
{
    /// <summary>One journal folder as listed for retention; <see cref="Error"/> is set (and the state unknown) when it cannot be read.</summary>
    public sealed class ReviewedFileJournalInfo
    {
        internal ReviewedFileJournalInfo(string journalPath, string planId, string label, string root, string state, DateTime lastActivityUtc, long totalBytes, string error)
        {
            JournalPath = journalPath; PlanId = planId; Label = label; Root = root; State = state;
            LastActivityUtc = lastActivityUtc; TotalBytes = totalBytes; Error = error;
        }
        public string JournalPath { get; }
        public string Directory => Path.GetDirectoryName(JournalPath)!;
        public string PlanId { get; }
        public string Label { get; }
        public string Root { get; }
        /// <summary>Journal state from its marker files; empty when unreadable.</summary>
        public string State { get; }
        /// <summary>Newest of the journal creation time and its state marker time (UTC).</summary>
        public DateTime LastActivityUtc { get; }
        public long TotalBytes { get; }
        public string Error { get; }
        /// <summary>Applied, RolledBack or Restored. Applying and incomplete journals may hold the only copy of original bytes.</summary>
        public bool IsCompleted => Error.Length == 0 && ReviewedFilePlan.IsCompletedJournalState(State);
    }

    /// <summary>
    /// Journal retention for <c>%LOCALAPPDATA%\TE2Enhanced\journal</c>: list, prune completed journals older than N days,
    /// explicit delete of one. Applying, RollbackIncomplete, RestoreIncomplete and unreadable journals are never pruned or
    /// deleted here, because they may hold the only copy of the user's original files. Only files a journal owns are removed.
    /// </summary>
    public sealed partial class ReviewedFilePlan
    {
        public const int DefaultJournalRetentionDays = 30;
        public const int MaxJournalRetentionDays = 3650;
        private static readonly Regex BackupName = new Regex(@"^backup-\d{3}\.bin$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        public static bool IsCompletedJournalState(string? state) =>
            state == ReviewedFileJournalState.Applied || state == ReviewedFileJournalState.RolledBack || state == ReviewedFileJournalState.Restored;

        /// <summary>Every journal under the root, newest first (bounded); unreadable ones are listed with an error, never hidden.</summary>
        public static IReadOnlyList<ReviewedFileJournalInfo> ListJournalInfos(string journalRoot, int max = 500)
        {
            if (string.IsNullOrWhiteSpace(journalRoot) || !System.IO.Directory.Exists(journalRoot)) return Array.Empty<ReviewedFileJournalInfo>();
            var list = new List<ReviewedFileJournalInfo>();
            foreach (var path in ListJournals(journalRoot, Math.Max(1, Math.Min(max, 500))))
                list.Add(ReadInfo(path));
            return list;
        }

        /// <summary>Completed journals whose last activity is older than <paramref name="olderThanDays"/> days.</summary>
        public static IReadOnlyList<ReviewedFileJournalInfo> SelectPrunableJournals(string journalRoot, DateTime nowUtc, int olderThanDays = DefaultJournalRetentionDays)
        {
            if (nowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Timestamp must be UTC.", nameof(nowUtc));
            if (olderThanDays < 1 || olderThanDays > MaxJournalRetentionDays) throw new ArgumentOutOfRangeException(nameof(olderThanDays));
            var cutoff = nowUtc.AddDays(-olderThanDays);
            return ListJournalInfos(journalRoot).Where(j => j.IsCompleted && j.LastActivityUtc < cutoff).ToArray();
        }

        /// <summary>Deletes the prunable journals; returns how many were removed. A journal that cannot be removed is left (reported by the count).</summary>
        public static int PruneJournals(string journalRoot, DateTime nowUtc, int olderThanDays = DefaultJournalRetentionDays)
        {
            var removed = 0;
            foreach (var journal in SelectPrunableJournals(journalRoot, nowUtc, olderThanDays))
            {
                try { DeleteJournal(journalRoot, journal.JournalPath); removed++; }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException) { }
            }
            return removed;
        }

        /// <summary>
        /// Explicit delete of one completed journal (journal.json first, then its backups and markers, then the folder when empty).
        /// Refuses journals outside <paramref name="journalRoot"/> and journals that are not completed.
        /// </summary>
        public static void DeleteJournal(string journalRoot, string journalPath)
        {
            if (string.IsNullOrWhiteSpace(journalRoot)) throw new ArgumentException("A journal folder is required.", nameof(journalRoot));
            var root = NormalizeRoot(journalRoot);
            var full = Path.GetFullPath(journalPath ?? throw new ArgumentNullException(nameof(journalPath)));
            var directory = Path.GetDirectoryName(full)!;
            if (!string.Equals(Path.GetFileName(full), JournalFileName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only journals directly under the journal folder can be deleted.", nameof(journalPath));
            var info = ReadInfo(full);
            if (!info.IsCompleted)
                throw new InvalidOperationException("Journal '" + info.Label + "' is " + (info.Error.Length > 0 ? "unreadable" : info.State) +
                    "; it may hold the only copy of the original files and is not deleted.");
            File.Delete(full); // the journal disappears from every list first; leftovers below are harmless
            foreach (var file in System.IO.Directory.GetFiles(directory))
            {
                var name = Path.GetFileName(file);
                if (BackupName.IsMatch(name) || IsMarkerName(name)) File.Delete(file);
            }
            if (!System.IO.Directory.EnumerateFileSystemEntries(directory).Any()) System.IO.Directory.Delete(directory, false);
        }

        private static bool IsMarkerName(string name) =>
            StateOrder.Any(s => string.Equals(name, s.ToLowerInvariant() + ".marker", StringComparison.OrdinalIgnoreCase));

        private static ReviewedFileJournalInfo ReadInfo(string journalPath)
        {
            var directory = Path.GetDirectoryName(journalPath)!;
            long bytes = 0;
            var last = DateTime.MinValue;
            try
            {
                foreach (var file in new DirectoryInfo(directory).GetFiles())
                {
                    bytes += file.Length;
                    if (IsMarkerName(file.Name) && file.LastWriteTimeUtc > last) last = file.LastWriteTimeUtc;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { }
            try
            {
                var journal = ReadJournal(journalPath);
                if (DateTime.TryParse(journal.CreatedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var created) &&
                    created > last) last = DateTime.SpecifyKind(created, DateTimeKind.Utc);
                if (last == DateTime.MinValue) last = File.GetLastWriteTimeUtc(journalPath);
                return new ReviewedFileJournalInfo(journalPath, journal.PlanId, journal.Label, journal.Root, journal.State, last, bytes, "");
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException ||
                                       ex is System.Runtime.Serialization.SerializationException || ex is ArgumentException || ex is FormatException)
            {
                if (last == DateTime.MinValue) { try { last = File.GetLastWriteTimeUtc(journalPath); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { } }
                return new ReviewedFileJournalInfo(journalPath, Path.GetFileName(directory), "", "", "", last, bytes, ex.Message.Length > 0 ? ex.Message : "Unreadable journal.");
            }
        }
    }
}
