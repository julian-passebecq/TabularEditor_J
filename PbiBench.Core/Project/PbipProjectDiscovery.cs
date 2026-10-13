using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PbiBench.Core.Project
{
    /// <summary>Bounded read-only inventory, not inferred PBIP associations.
    /// Cancellation is cooperative between filesystem calls; network calls can block.</summary>
    public static class PbipProjectDiscovery
    {
        public const int MaxDirectories = 512;
        public const int MaxEntriesPerDirectory = 512;
        public const int MaxScanDepth = 4;
        private const int MaxAncestorWalk = 10;
        private static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".git", ".vs", ".idea", "bin", "obj", "node_modules", ".pbibench", ".pbi", ".tools" };

        public static PbipProjectContext Discover(string sourcePath, GitProjectSummary? git = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) throw new ArgumentException("A source path is required.", nameof(sourcePath));
            cancellationToken.ThrowIfCancellationRequested();
            var source = Path.GetFullPath(sourcePath);
            var start = Directory.Exists(source) ? source : File.Exists(source) ? Path.GetDirectoryName(source)! :
                throw new FileNotFoundException("The source path does not exist.");
            var scan = new Scan(cancellationToken);
            var root = start;
            string? gitRoot = null;
            var foundPbip = false;
            var current = new DirectoryInfo(start);
            for (var i = 0; current != null && i < MaxAncestorWalk; i++, current = current.Parent)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scan.IsReparse(current.FullName)) break;
                if (gitRoot == null && GitMarkerExists(current.FullName)) gitRoot = current.FullName;
                if (scan.Files(current.FullName, "*.pbip").Length > 0) { root = current.FullName; foundPbip = true; break; }
                if (i == MaxAncestorWalk - 1 && current.Parent != null) scan.Note("Ancestor search limit reached.");
            }
            if (!foundPbip && gitRoot != null) root = gitRoot;
            current = new DirectoryInfo(root);
            for (var i = 0; gitRoot == null && current != null && i < MaxAncestorWalk; i++, current = current.Parent)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (scan.IsReparse(current.FullName)) break;
                if (GitMarkerExists(current.FullName)) gitRoot = current.FullName;
            }
            var directories = scan.Directories(root).ToArray();
            var candidates = directories.SelectMany(d => scan.Files(d, "*.pbip"))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            if (candidates.Length == MaxDirectories) scan.Note("PBIP candidate limit reached.");
            var semantic = directories.Where(d => d.EndsWith(".SemanticModel", StringComparison.OrdinalIgnoreCase)).ToArray();
            var reports = directories.Where(d => d.EndsWith(".Report", StringComparison.OrdinalIgnoreCase)).ToArray();
            var tmdl = directories.Any(d => semantic.Any(s => IsWithin(d, s)) && scan.Files(d, "*.tmdl").Length > 0);
            var selected = File.Exists(source) && string.Equals(Path.GetExtension(source), ".pbip", StringComparison.OrdinalIgnoreCase) ? source : null;
            if (selected != null && !candidates.Contains(selected, StringComparer.OrdinalIgnoreCase))
                candidates = new[] { selected }.Concat(candidates.Take(MaxDirectories - 1)).ToArray();
            if (selected == null && candidates.Length == 1 && !scan.Incomplete) selected = candidates[0];
            if (candidates.Length > 1 && selected == null) scan.Information("Multiple PBIP files found; explicit selection required.");
            var summary = git ?? new GitProjectSummary { Root = gitRoot, Detected = gitRoot != null, State = GitState.Unknown };
            return new PbipProjectContext {
                SourcePath = source, ProjectRoot = root, PbipFile = selected, PbipCandidates = candidates,
                SemanticModelFolders = semantic, ReportFolders = reports, Git = summary,
                DiscoveryIncomplete = scan.Incomplete, DiscoveryDiagnostics = scan.Diagnostics.ToArray(),
                Capabilities = new ProjectCapabilities { Pbip = candidates.Length > 0, Tmdl = tmdl,
                    Pbir = reports.Any(r => File.Exists(Path.Combine(r, "definition.pbir"))), Git = summary.Detected }
            };
        }

        private static bool IsWithin(string path, string root) => string.Equals(path, root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        private static bool GitMarkerExists(string path) => Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"));

        private sealed class Scan
        {
            private readonly CancellationToken _token;
            public bool Incomplete { get; private set; }
            public readonly List<string> Diagnostics = new List<string>();
            public Scan(CancellationToken token) { _token = token; }
            public void Information(string message) { if (!Diagnostics.Contains(message)) Diagnostics.Add(message); }
            public void Note(string message) { Incomplete = true; Information(message); }
            public bool IsReparse(string path)
            {
                try {
                    if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0) return false;
                    Note("Reparse directory skipped."); return true;
                }
                catch (IOException) { Note("Unreadable directory skipped."); return true; }
                catch (UnauthorizedAccessException) { Note("Access-denied directory skipped."); return true; }
            }
            public string[] Files(string path, string pattern) => Entries(path, pattern, false);
            private string[] Entries(string path, string pattern, bool directories)
            {
                _token.ThrowIfCancellationRequested();
                var entries = new List<string>();
                try {
                    foreach (var entry in directories ? Directory.EnumerateDirectories(path) : Directory.EnumerateFiles(path, pattern))
                    {
                        _token.ThrowIfCancellationRequested();
                        if (entries.Count == MaxEntriesPerDirectory) { Note("Directory entry limit reached."); break; }
                        entries.Add(entry);
                    }
                }
                catch (IOException) { Note("Unreadable directory skipped."); }
                catch (UnauthorizedAccessException) { Note("Access-denied directory skipped."); }
                return entries.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            public IEnumerable<string> Directories(string root)
            {
                var queue = new Queue<Tuple<string, int>>();
                queue.Enqueue(Tuple.Create(root, 0));
                var count = 0;
                while (queue.Count > 0)
                {
                    _token.ThrowIfCancellationRequested();
                    var entry = queue.Dequeue();
                    if (IsReparse(entry.Item1)) continue;
                    yield return entry.Item1;
                    count++;
                    foreach (var child in Entries(entry.Item1, "*", true))
                    {
                        if (Ignored.Contains(Path.GetFileName(child))) continue;
                        if (entry.Item2 >= MaxScanDepth) { Note("Discovery depth limit reached."); break; }
                        if (count + queue.Count >= MaxDirectories) { Note("Directory count limit reached."); break; }
                        if (IsReparse(child)) continue;
                        queue.Enqueue(Tuple.Create(child, entry.Item2 + 1));
                    }
                }
            }
        }
    }
}
