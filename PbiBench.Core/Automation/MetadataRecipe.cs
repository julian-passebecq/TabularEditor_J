using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PbiBench.Core.Automation
{
    public enum MetadataRecipeOperation { SetDisplayFolder, SetHidden }

    public sealed class MeasureMetadata
    {
        public MeasureMetadata(string id, string folder, bool hidden) { Id = id; Folder = folder ?? ""; Hidden = hidden; }
        public string Id { get; }
        public string Folder { get; }
        public bool Hidden { get; }
    }
    public sealed class MetadataChange
    {
        internal MetadataChange(string id, string before, string after) { Id = id; Before = before; After = after; }
        public string Id { get; }
        public string Before { get; }
        public string After { get; }
    }
    public sealed class MetadataRecipePlan
    {
        internal MetadataRecipePlan(string fingerprint, MetadataRecipeOperation operation, IEnumerable<MetadataChange> changes)
        { Fingerprint = fingerprint; Operation = operation; Changes = Array.AsReadOnly(changes.ToArray()); }
        public string Fingerprint { get; }
        public MetadataRecipeOperation Operation { get; }
        public IReadOnlyList<MetadataChange> Changes { get; }
    }
    public static class MetadataRecipe
    {
        public const int MaxMeasures = 10000;
        public const int MaxChanges = 1000;
        public static MetadataRecipePlan Propose(IEnumerable<MeasureMetadata> source, IEnumerable<string> targetIds, MetadataRecipeOperation operation, string value)
        {
            if (!Enum.IsDefined(typeof(MetadataRecipeOperation), operation)) throw new ArgumentException("Recipe operation is not allowlisted.");
            if (value == null || value.Length > 1024 || value.IndexOf('\0') >= 0) throw new ArgumentException("Invalid metadata value.");
            if (operation == MetadataRecipeOperation.SetHidden && value != "True" && value != "False") throw new ArgumentException("Hidden must be True or False.");
            var snapshot = Snapshot(source);
            var byId = snapshot.ToDictionary(x => x.Id, StringComparer.Ordinal);
            var targets = targetIds.Take(MaxChanges + 1).ToArray();
            if (targets.Length > MaxChanges) throw new ArgumentException("Recipe is limited to 1,000 measures.");
            var changes = new List<MetadataChange>();
            foreach (var id in targets.Distinct(StringComparer.Ordinal))
            {
                if (!byId.TryGetValue(id, out var item)) throw new ArgumentException("Recipe target is not in the source snapshot.");
                var before = operation == MetadataRecipeOperation.SetDisplayFolder ? item.Folder : item.Hidden.ToString();
                if (before != value) changes.Add(new MetadataChange(id, before, value));
            }
            return new MetadataRecipePlan(Fingerprint(snapshot), operation, changes);
        }
        public static bool IsCurrent(MetadataRecipePlan plan, IEnumerable<MeasureMetadata> source) => plan.Fingerprint == Fingerprint(Snapshot(source));
        private static MeasureMetadata[] Snapshot(IEnumerable<MeasureMetadata> source)
        {
            var items = source.Take(MaxMeasures + 1).ToArray();
            if (items.Length > MaxMeasures || items.Any(x => x == null || string.IsNullOrEmpty(x.Id)) ||
                items.Sum(x => (long)x.Id.Length + x.Folder.Length) > 4 * 1024 * 1024 ||
                items.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != items.Length) throw new ArgumentException("Invalid or oversized metadata snapshot.");
            return items.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        }
        private static string Fingerprint(MeasureMetadata[] source)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                    foreach (var item in source) { writer.Write(item.Id); writer.Write(item.Folder); writer.Write(item.Hidden); }
                using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
            }
        }
    }
}
