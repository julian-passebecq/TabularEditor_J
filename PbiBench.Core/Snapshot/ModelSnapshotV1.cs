using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace PbiBench.Core.Snapshot
{
    /// <summary>
    /// Versioned, read-only model metadata snapshot exchanged with external local tools (the
    /// TE2 Enhanced MCP companion). It carries metadata only: no connection strings, credentials
    /// or data rows. Measure expressions are present only when the publisher opted in.
    /// </summary>
    [DataContract]
    public sealed class ModelSnapshotV1
    {
        public const int CurrentContractVersion = 1;

        [DataMember(Order = 1)] public int ContractVersion { get; set; } = CurrentContractVersion;
        [DataMember(Order = 2)] public string Producer { get; set; } = "TE2 Enhanced";
        [DataMember(Order = 3)] public string PublishedAtUtc { get; set; } = "";
        [DataMember(Order = 4)] public string ModelName { get; set; } = "";
        /// <summary>Opaque publisher revision; changes whenever the loaded model changed.</summary>
        [DataMember(Order = 5)] public string ModelRevision { get; set; } = "";
        [DataMember(Order = 6)] public int CompatibilityLevel { get; set; }
        [DataMember(Order = 7)] public bool IncludesExpressions { get; set; }
        [DataMember(Order = 8)] public List<SnapshotTable> Tables { get; set; } = new List<SnapshotTable>();
        [DataMember(Order = 9)] public List<SnapshotRelationship> Relationships { get; set; } = new List<SnapshotRelationship>();
        [DataMember(Order = 10)] public List<SnapshotSelection> Selection { get; set; } = new List<SnapshotSelection>();
    }

    [DataContract]
    public sealed class SnapshotTable
    {
        [DataMember(Order = 1)] public string Name { get; set; } = "";
        [DataMember(Order = 2)] public bool IsHidden { get; set; }
        [DataMember(Order = 3)] public string Description { get; set; } = "";
        [DataMember(Order = 4)] public List<SnapshotColumn> Columns { get; set; } = new List<SnapshotColumn>();
        [DataMember(Order = 5)] public List<SnapshotMeasure> Measures { get; set; } = new List<SnapshotMeasure>();
    }

    [DataContract]
    public sealed class SnapshotColumn
    {
        [DataMember(Order = 1)] public string Name { get; set; } = "";
        [DataMember(Order = 2)] public string DataType { get; set; } = "";
        [DataMember(Order = 3)] public bool IsHidden { get; set; }
        [DataMember(Order = 4)] public string DisplayFolder { get; set; } = "";
    }

    [DataContract]
    public sealed class SnapshotMeasure
    {
        [DataMember(Order = 1)] public string Name { get; set; } = "";
        [DataMember(Order = 2)] public string DisplayFolder { get; set; } = "";
        [DataMember(Order = 3)] public string FormatString { get; set; } = "";
        [DataMember(Order = 4)] public bool IsHidden { get; set; }
        [DataMember(Order = 5)] public string Description { get; set; } = "";
        /// <summary>Null unless <see cref="ModelSnapshotV1.IncludesExpressions"/> is true.</summary>
        [DataMember(Order = 6, EmitDefaultValue = false)] public string? Expression { get; set; }
    }

    [DataContract]
    public sealed class SnapshotRelationship
    {
        [DataMember(Order = 1)] public string FromTable { get; set; } = "";
        [DataMember(Order = 2)] public string FromColumn { get; set; } = "";
        [DataMember(Order = 3)] public string ToTable { get; set; } = "";
        [DataMember(Order = 4)] public string ToColumn { get; set; } = "";
        [DataMember(Order = 5)] public bool IsActive { get; set; }
        [DataMember(Order = 6)] public string CrossFilter { get; set; } = "";
    }

    [DataContract]
    public sealed class SnapshotSelection
    {
        /// <summary>Table, Column, Measure, Relationship, ... (TE2 object type name).</summary>
        [DataMember(Order = 1)] public string Kind { get; set; } = "";
        [DataMember(Order = 2)] public string Table { get; set; } = "";
        [DataMember(Order = 3)] public string Name { get; set; } = "";
    }

    public static class ModelSnapshotSerializer
    {
        public const string FileName = "snapshot-v1.json";
        public const int MaxBytes = 32 * 1024 * 1024;
        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(ModelSnapshotV1));
        private static readonly string[] CredentialMarkers = { "password=", "pwd=", "accountkey=", "client_secret", "sharedaccesssignature=" };

        /// <summary>Default per-user location; never inside a repository.</summary>
        public static string DefaultPath(string localAppData) =>
            Path.Combine(localAppData, "TE2Enhanced", "mcp", FileName);

        public static string Serialize(ModelSnapshotV1 snapshot)
        {
            Validate(snapshot);
            using (var stream = new MemoryStream())
            {
                Serializer.WriteObject(stream, snapshot);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static ModelSnapshotV1 Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Snapshot is empty.");
            if (Encoding.UTF8.GetByteCount(json) > MaxBytes) throw new InvalidDataException("Snapshot exceeds the 32 MiB limit.");
            ModelSnapshotV1 snapshot;
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                snapshot = Serializer.ReadObject(stream) as ModelSnapshotV1 ?? throw new InvalidDataException("Snapshot JSON could not be read.");
            Validate(snapshot);
            return snapshot;
        }

        public static ModelSnapshotV1 ReadFile(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("No published TE2 Enhanced snapshot.", path);
            if (info.Length > MaxBytes) throw new InvalidDataException("Snapshot exceeds the 32 MiB limit.");
            return Deserialize(File.ReadAllText(path, Encoding.UTF8));
        }

        /// <summary>Atomic replace so readers never observe a partial snapshot.</summary>
        public static void WriteFile(string path, ModelSnapshotV1 snapshot)
        {
            var destination = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? throw new InvalidOperationException("No directory."));
            var json = Serialize(snapshot);
            var temp = destination + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temp, json, new UTF8Encoding(false));
                if (File.Exists(destination)) File.Replace(temp, destination, null);
                else File.Move(temp, destination);
            }
            finally { try { if (File.Exists(temp)) File.Delete(temp); } catch { } }
        }

        public static void Validate(ModelSnapshotV1 snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.ContractVersion != ModelSnapshotV1.CurrentContractVersion)
                throw new InvalidDataException($"Unsupported snapshot contract version {snapshot.ContractVersion}.");
            snapshot.Tables ??= new List<SnapshotTable>();
            snapshot.Relationships ??= new List<SnapshotRelationship>();
            snapshot.Selection ??= new List<SnapshotSelection>();
            foreach (var table in snapshot.Tables)
            {
                table.Columns ??= new List<SnapshotColumn>();
                table.Measures ??= new List<SnapshotMeasure>();
                if (!snapshot.IncludesExpressions && table.Measures.Any(m => m.Expression != null))
                    throw new InvalidDataException("Snapshot carries expressions without the IncludesExpressions opt-in.");
            }
            foreach (var text in Strings(snapshot))
            {
                var lower = text.ToLowerInvariant();
                if (CredentialMarkers.Any(lower.Contains))
                    throw new InvalidDataException("Snapshot text looks like it contains a credential; refusing to exchange it.");
            }
        }

        private static IEnumerable<string> Strings(ModelSnapshotV1 s)
        {
            yield return s.ModelName;
            foreach (var t in s.Tables)
            {
                yield return t.Name; yield return t.Description;
                foreach (var c in t.Columns) { yield return c.Name; yield return c.DisplayFolder; }
                foreach (var m in t.Measures) { yield return m.Name; yield return m.Description; yield return m.FormatString; if (m.Expression != null) yield return m.Expression; }
            }
        }
    }
}
