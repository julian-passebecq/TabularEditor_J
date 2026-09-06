using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PbiBench.Core.Project
{
    /// <summary>
    /// Bounded, read-only discovery of a local Power BI project. It never executes Git,
    /// opens network connections or reads model/report contents.
    /// </summary>
    public static class PbipProjectDiscovery
    {
        private const int MaxAncestorWalk = 10;
        private const int MaxScanDepth = 4;
        private const int MaxDirectories = 512;

        private static readonly HashSet<string> IgnoredDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".git", ".vs", ".idea", "bin", "obj", "node_modules", ".pbibench", ".pbi"
        };

        public static PbipProjectContext Discover(string sourcePath, GitProjectSummary? git = null)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new ArgumentException("A source file or directory is required.", nameof(sourcePath));

            var fullSource = Path.GetFullPath(sourcePath);
            var startDirectory = ResolveStartDirectory(fullSource);
            var projectRoot = FindProjectRoot(startDirectory);
            var directories = EnumerateDirectoriesBounded(projectRoot).ToArray();

            var pbip = FindPbip(projectRoot);
            var semanticFolders = directories
                .Where(IsSemanticModelFolder)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var reportFolders = directories
                .Where(IsReportFolder)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var gitRoot = FindGitRoot(projectRoot);
            var gitSummary = git ?? new GitProjectSummary
            {
                Detected = gitRoot != null,
                Root = gitRoot,
                State = GitState.Unknown
            };

            if (gitSummary.Root == null && gitRoot != null)
                gitSummary.Root = gitRoot;
            if (gitRoot != null)
                gitSummary.Detected = true;

            return new PbipProjectContext
            {
                SourcePath = fullSource,
                ProjectRoot = projectRoot,
                PbipFile = pbip,
                SemanticModelFolders = semanticFolders,
                ReportFolders = reportFolders,
                Git = gitSummary,
                Capabilities = new ProjectCapabilities
                {
                    Pbip = pbip != null,
                    Tmdl = semanticFolders.Any(ContainsTmdlDefinition),
                    Pbir = reportFolders.Any(ContainsPbirDefinition),
                    Git = gitSummary.Detected
                }
            };
        }

        private static string ResolveStartDirectory(string fullSource)
        {
            if (Directory.Exists(fullSource))
                return TrimEndingSeparator(fullSource);
            if (File.Exists(fullSource))
                return Path.GetDirectoryName(fullSource) ?? throw new InvalidOperationException("Could not resolve source directory.");

            throw new FileNotFoundException("The source path does not exist.", fullSource);
        }

        private static string FindProjectRoot(string startDirectory)
        {
            var current = new DirectoryInfo(startDirectory);
            DirectoryInfo? nearestGit = null;

            for (var i = 0; current != null && i < MaxAncestorWalk; i++, current = current.Parent)
            {
                if (SafeEnumerateFiles(current.FullName, "*.pbip").Any())
                    return current.FullName;

                if (nearestGit == null && GitMarkerExists(current.FullName))
                    nearestGit = current;
            }

            return nearestGit?.FullName ?? startDirectory;
        }

        private static string? FindPbip(string projectRoot)
        {
            return SafeEnumerateFiles(projectRoot, "*.pbip")
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }

        private static IEnumerable<string> EnumerateDirectoriesBounded(string root)
        {
            var count = 0;
            var queue = new Queue<DirectoryDepth>();
            queue.Enqueue(new DirectoryDepth(root, 0));

            while (queue.Count > 0 && count < MaxDirectories)
            {
                var current = queue.Dequeue();
                yield return current.Path;
                count++;

                if (current.Depth >= MaxScanDepth)
                    continue;

                foreach (var child in SafeEnumerateDirectories(current.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    if (count + queue.Count >= MaxDirectories)
                        break;
                    if (ShouldSkipDirectory(child))
                        continue;
                    queue.Enqueue(new DirectoryDepth(child, current.Depth + 1));
                }
            }
        }

        private static IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            try { return Directory.EnumerateDirectories(path).ToArray(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
            catch (IOException) { return Array.Empty<string>(); }
        }

        private static IEnumerable<string> SafeEnumerateFiles(string path, string pattern)
        {
            try { return Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly).ToArray(); }
            catch (UnauthorizedAccessException) { return Array.Empty<string>(); }
            catch (IOException) { return Array.Empty<string>(); }
        }

        private static bool ShouldSkipDirectory(string path)
        {
            var name = Path.GetFileName(path);
            if (IgnoredDirectoryNames.Contains(name))
                return true;

            try
            {
                return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        private static bool IsSemanticModelFolder(string path)
            => path.EndsWith(".SemanticModel", StringComparison.OrdinalIgnoreCase);

        private static bool IsReportFolder(string path)
            => path.EndsWith(".Report", StringComparison.OrdinalIgnoreCase);

        private static bool ContainsTmdlDefinition(string semanticFolder)
        {
            var definition = Path.Combine(semanticFolder, "definition");
            if (!Directory.Exists(definition)) return false;
            try { return Directory.EnumerateFiles(definition, "*.tmdl", SearchOption.AllDirectories).Take(1).Any(); }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        private static bool ContainsPbirDefinition(string reportFolder)
        {
            if (File.Exists(Path.Combine(reportFolder, "definition.pbir"))) return true;
            return Directory.Exists(Path.Combine(reportFolder, "definition"));
        }

        private static string? FindGitRoot(string start)
        {
            var current = new DirectoryInfo(start);
            for (var i = 0; current != null && i < MaxAncestorWalk; i++, current = current.Parent)
            {
                if (GitMarkerExists(current.FullName)) return current.FullName;
            }
            return null;
        }

        private static bool GitMarkerExists(string path)
            => Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git"));

        private static string TrimEndingSeparator(string path)
            => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private readonly struct DirectoryDepth
        {
            public DirectoryDepth(string path, int depth)
            {
                Path = path;
                Depth = depth;
            }

            public string Path { get; }
            public int Depth { get; }
        }
    }
}
