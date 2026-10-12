using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PbiBench.Core.Project
{
    public sealed class FileManifestEntry
    {
        public FileManifestEntry(string relativePath, long length, DateTime lastWriteUtc, string? hash)
        { RelativePath = relativePath; Length = length; LastWriteUtc = lastWriteUtc; Hash = hash; }
        public string RelativePath { get; }
        public long Length { get; }
        public DateTime LastWriteUtc { get; }
        /// <summary>SHA-256, or null when the file exceeded the hash size limit or could not be read.</summary>
        public string? Hash { get; }
    }

    /// <summary>Bounded content manifest of a project folder (relative paths, sizes, hashes).</summary>
    public sealed class FileManifest
    {
        public const int MaxFiles = 4096;
        public const long MaxHashBytes = 32L * 1024 * 1024;
        public const int MaxDepth = 12;
        private static readonly HashSet<string> IgnoredFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".git", ".vs", ".idea", "bin", "obj", "node_modules", ".pbi", ".tools" };

        public FileManifest(string root, IEnumerable<FileManifestEntry> entries, bool incomplete, DateTime capturedUtc)
        {
            Root = root;
            Entries = entries.OrderBy(e => e.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
            Incomplete = incomplete;
            CapturedUtc = capturedUtc;
            Fingerprint = ReviewedFilePlan.Hash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",
                Entries.Select(e => e.RelativePath.ToUpperInvariant() + "|" + e.Length + "|" + (e.Hash ?? e.LastWriteUtc.Ticks.ToString())))));
        }

        public string Root { get; }
        public IReadOnlyList<FileManifestEntry> Entries { get; }
        public bool Incomplete { get; }
        public DateTime CapturedUtc { get; }
        public string Fingerprint { get; }

        /// <summary>Ignored folder names (also used by the host watcher to drop noise events).</summary>
        public static bool IsIgnoredPath(string relativePath)
        {
            var segments = relativePath.Split('/', '\\');
            for (var i = 0; i < segments.Length - 1; i++) if (IgnoredFolders.Contains(segments[i])) return true;
            var name = segments[segments.Length - 1];
            return name.IndexOf(".te2tmp-", StringComparison.OrdinalIgnoreCase) >= 0 || IgnoredFolders.Contains(name);
        }

        public static FileManifest Capture(string root, CancellationToken token = default)
        {
            var normalized = ReviewedFilePlan.NormalizeRoot(root);
            if (!Directory.Exists(normalized)) throw new DirectoryNotFoundException("The project folder does not exist.");
            var entries = new List<FileManifestEntry>();
            var incomplete = false;
            var stack = new Stack<Tuple<string, int>>();
            stack.Push(Tuple.Create(normalized, 0));
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var current = stack.Pop();
                string[] files, folders;
                try
                {
                    if ((File.GetAttributes(current.Item1) & FileAttributes.ReparsePoint) != 0 && current.Item2 > 0) { incomplete = true; continue; }
                    files = Directory.GetFiles(current.Item1);
                    folders = Directory.GetDirectories(current.Item1);
                }
                catch (IOException) { incomplete = true; continue; }
                catch (UnauthorizedAccessException) { incomplete = true; continue; }
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();
                    var relative = file.Substring(normalized.Length + 1).Replace('\\', '/');
                    if (IsIgnoredPath(relative)) continue;
                    if (entries.Count == MaxFiles) { incomplete = true; break; }
                    try
                    {
                        var info = new FileInfo(file);
                        string? hash = null;
                        if (info.Length <= MaxHashBytes) { try { hash = ReviewedFilePlan.HashFile(file); } catch (IOException) { incomplete = true; } }
                        entries.Add(new FileManifestEntry(relative, info.Length, info.LastWriteTimeUtc, hash));
                    }
                    catch (IOException) { incomplete = true; }
                    catch (UnauthorizedAccessException) { incomplete = true; }
                }
                if (entries.Count == MaxFiles) { incomplete = incomplete || stack.Count > 0 || folders.Length > 0; break; }
                foreach (var folder in folders)
                {
                    if (IgnoredFolders.Contains(Path.GetFileName(folder))) continue;
                    if (current.Item2 + 1 > MaxDepth) { incomplete = true; continue; }
                    stack.Push(Tuple.Create(folder, current.Item2 + 1));
                }
            }
            return new FileManifest(normalized, entries, incomplete, DateTime.UtcNow);
        }
    }

    public enum ExternalChangeKind { Added, Removed, Modified, Renamed }

    public sealed class ExternalChange
    {
        public ExternalChange(ExternalChangeKind kind, string path, string? previousPath = null)
        { Kind = kind; Path = path; PreviousPath = previousPath; }
        public ExternalChangeKind Kind { get; }
        public string Path { get; }
        public string? PreviousPath { get; }
        public override string ToString() => Kind == ExternalChangeKind.Renamed ? Kind + " " + PreviousPath + " -> " + Path : Kind + " " + Path;
    }

    public sealed class ExternalChangeSet
    {
        public ExternalChangeSet(IReadOnlyList<ExternalChange> changes, bool incomplete) { Changes = changes; Incomplete = incomplete; }
        public IReadOnlyList<ExternalChange> Changes { get; }
        /// <summary>True when either manifest was bounded/incomplete, so absence of a change is not proof.</summary>
        public bool Incomplete { get; }
        public bool HasChanges => Changes.Count > 0;
        public int Count(ExternalChangeKind kind) => Changes.Count(c => c.Kind == kind);
    }

    /// <summary>Pure manifest diff. A removed+added pair with the same non-null content hash (unique
    /// on both sides) is reported as a rename; anything ambiguous stays as add/remove.</summary>
    public static class ExternalChangeDetector
    {
        public static ExternalChangeSet Diff(FileManifest before, FileManifest after)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));
            var old = before.Entries.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
            var now = after.Entries.ToDictionary(e => e.RelativePath, StringComparer.OrdinalIgnoreCase);
            var changes = new List<ExternalChange>();
            var removed = old.Values.Where(e => !now.ContainsKey(e.RelativePath)).ToList();
            var added = now.Values.Where(e => !old.ContainsKey(e.RelativePath)).ToList();
            foreach (var entry in now.Values)
                if (old.TryGetValue(entry.RelativePath, out var prior) && !Same(prior, entry))
                    changes.Add(new ExternalChange(ExternalChangeKind.Modified, entry.RelativePath));
            foreach (var gone in removed.Where(r => r.Hash != null).ToArray())
            {
                var removedSameHash = removed.Count(r => r.Hash == gone.Hash);
                var candidates = added.Where(a => a.Hash == gone.Hash).ToArray();
                if (removedSameHash != 1 || candidates.Length != 1) continue;
                changes.Add(new ExternalChange(ExternalChangeKind.Renamed, candidates[0].RelativePath, gone.RelativePath));
                removed.Remove(gone); added.Remove(candidates[0]);
            }
            changes.AddRange(removed.Select(r => new ExternalChange(ExternalChangeKind.Removed, r.RelativePath)));
            changes.AddRange(added.Select(a => new ExternalChange(ExternalChangeKind.Added, a.RelativePath)));
            return new ExternalChangeSet(changes.OrderBy(c => c.Path, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Kind).ToArray(),
                before.Incomplete || after.Incomplete);
        }

        private static bool Same(FileManifestEntry a, FileManifestEntry b)
        {
            if (a.Length != b.Length) return false;
            if (a.Hash != null && b.Hash != null) return a.Hash == b.Hash;
            return a.LastWriteUtc == b.LastWriteUtc;
        }
    }
}
