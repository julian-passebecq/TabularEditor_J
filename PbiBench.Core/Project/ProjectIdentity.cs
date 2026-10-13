using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using PbiBench.Core.IO;

namespace PbiBench.Core.Project
{
    /// <summary>
    /// Stable local identity of a PBIP project: hash of the normalized root plus the selected .pbip
    /// relative path (case-insensitive). The .pbip content hash is kept separately as a stamp, so
    /// editing the .pbip does not change identity but is still visible.
    /// </summary>
    public sealed class ProjectIdentity
    {
        private ProjectIdentity(string id, string root, string? pbipRelative, string? pbipContentHash)
        { Id = id; NormalizedRoot = root; PbipRelativePath = pbipRelative; PbipContentHash = pbipContentHash; }

        public string Id { get; }
        public string NormalizedRoot { get; }
        public string? PbipRelativePath { get; }
        public string? PbipContentHash { get; }
        public string DisplayName => PbipRelativePath != null ? Path.GetFileNameWithoutExtension(PbipRelativePath) : Path.GetFileName(NormalizedRoot);

        public static ProjectIdentity Create(string projectRoot, string? pbipFile)
        {
            if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("A project root is required.", nameof(projectRoot));
            var root = ReviewedFilePlan.NormalizeRoot(projectRoot);
            string? relative = null, contentHash = null;
            if (!string.IsNullOrWhiteSpace(pbipFile))
            {
                var full = Path.GetFullPath(pbipFile);
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The PBIP file must be inside the project root.", nameof(pbipFile));
                relative = full.Substring(root.Length + 1).Replace('\\', '/');
                if (File.Exists(full) && new FileInfo(full).Length <= 4 * 1024 * 1024) contentHash = ReviewedFilePlan.HashFile(full);
            }
            var key = root.Replace('\\', '/').ToUpperInvariant() + "\n" + (relative ?? "").ToUpperInvariant();
            using (var sha = SHA256.Create())
                return new ProjectIdentity(ReviewedFilePlan.ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Substring(0, 16), root, relative, contentHash);
        }
    }

    [DataContract]
    public sealed class ProjectRegistryEntry
    {
        [DataMember(Name = "id", Order = 1)] public string Id { get; set; } = "";
        [DataMember(Name = "root", Order = 2)] public string Root { get; set; } = "";
        [DataMember(Name = "pbip", Order = 3, EmitDefaultValue = false)] public string? Pbip { get; set; }
        [DataMember(Name = "name", Order = 4)] public string Name { get; set; } = "";
        [DataMember(Name = "lastOpenedUtc", Order = 5)] public string LastOpenedUtc { get; set; } = "";
        [DataMember(Name = "pbipHash", Order = 6, EmitDefaultValue = false)] public string? PbipHash { get; set; }
    }

    [DataContract]
    public sealed class ProjectRegistryDocument
    {
        [DataMember(Name = "version", Order = 1)] public int Version { get; set; } = 1;
        [DataMember(Name = "projects", Order = 2)] public ProjectRegistryEntry[] Projects { get; set; } = Array.Empty<ProjectRegistryEntry>();
    }

    /// <summary>
    /// Per-user recent-project registry (paths and hashes only, no model content). It lives under
    /// %LOCALAPPDATA%\TE2Enhanced by default and refuses any location inside a registered project,
    /// so it is never committed with a repository.
    /// </summary>
    public sealed class ProjectRegistry
    {
        public const int MaxEntries = 100;
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(ProjectRegistryDocument));
        private readonly List<ProjectRegistryEntry> _entries;
        private ProjectRegistry(string path, List<ProjectRegistryEntry> entries) { FilePath = path; _entries = entries; }

        public string FilePath { get; }
        public IReadOnlyList<ProjectRegistryEntry> Entries => _entries;

        public static string DefaultPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TE2Enhanced", "projects.json");

        /// <summary>Loads the registry; a missing, oversized or unreadable file yields an empty registry (never throws for content).</summary>
        public static ProjectRegistry Load(string path)
        {
            var full = Path.GetFullPath(path);
            var entries = new List<ProjectRegistryEntry>();
            try
            {
                var info = new FileInfo(full);
                if (info.Exists && info.Length <= 1024 * 1024)
                    using (var stream = File.OpenRead(full))
                        if (Serializer.ReadObject(stream) is ProjectRegistryDocument document && document.Version == 1 && document.Projects != null)
                            entries.AddRange(document.Projects.Where(p => p != null && p.Id.Length > 0 && p.Root.Length > 0).Take(MaxEntries));
            }
            catch (IOException) { } catch (UnauthorizedAccessException) { } catch (SerializationException) { }
            return new ProjectRegistry(full, entries);
        }

        public ProjectRegistryEntry Touch(ProjectIdentity identity, DateTime utcNow)
        {
            if (identity == null) throw new ArgumentNullException(nameof(identity));
            if (IsInside(FilePath, identity.NormalizedRoot)) throw new InvalidOperationException("The registry may not live inside a project folder.");
            _entries.RemoveAll(e => e.Id == identity.Id);
            var entry = new ProjectRegistryEntry { Id = identity.Id, Root = identity.NormalizedRoot, Pbip = identity.PbipRelativePath,
                Name = identity.DisplayName, PbipHash = identity.PbipContentHash, LastOpenedUtc = utcNow.ToUniversalTime().ToString("o") };
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries) _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            return entry;
        }

        public ProjectRegistryEntry? Find(string id) => _entries.FirstOrDefault(e => e.Id == id);

        public bool Forget(string id) => _entries.RemoveAll(e => e.Id == id) > 0;

        public void Save()
        {
            foreach (var entry in _entries)
                if (IsInside(FilePath, entry.Root)) throw new InvalidOperationException("The registry may not live inside a project folder.");
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            byte[] bytes;
            using (var buffer = new MemoryStream()) { Serializer.WriteObject(buffer, new ProjectRegistryDocument { Projects = _entries.ToArray() }); bytes = buffer.ToArray(); }
            // Single temp write + replace per save; no retry loop (OBS-001).
            var temp = FilePath + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            try
            {
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(FilePath)) SafeFileReplace.Replace(temp, FilePath, null, true); else File.Move(temp, FilePath);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static bool IsInside(string path, string root) =>
            path.StartsWith(ReviewedFilePlan.NormalizeRoot(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
