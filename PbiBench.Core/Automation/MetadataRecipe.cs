using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PbiBench.Core.Automation
{
    // Values are appended only; existing numeric values stay stable.
    public enum MetadataRecipeOperation { SetDisplayFolder, SetHidden, SetFormatString, SetDescription, Rename }

    public sealed class MeasureMetadata
    {
        public MeasureMetadata(string id, string folder, bool hidden) : this(id, "", folder, hidden, "", "", false) { }
        public MeasureMetadata(string id, string name, string folder, bool hidden, string formatString, string description, bool hasFormatStringExpression)
        {
            Id = id; Name = name ?? ""; Folder = folder ?? ""; Hidden = hidden; FormatString = formatString ?? ""; Description = description ?? "";
            HasFormatStringExpression = hasFormatStringExpression;
        }
        public string Id { get; }
        public string Name { get; }
        public string Folder { get; }
        public bool Hidden { get; }
        public string FormatString { get; }
        public string Description { get; }
        public bool HasFormatStringExpression { get; }

        public string Get(MetadataRecipeOperation operation)
        {
            switch (operation)
            {
                case MetadataRecipeOperation.SetDisplayFolder: return Folder;
                case MetadataRecipeOperation.SetHidden: return Hidden.ToString();
                case MetadataRecipeOperation.SetFormatString: return FormatString;
                case MetadataRecipeOperation.SetDescription: return Description;
                case MetadataRecipeOperation.Rename: return Name;
                default: throw new ArgumentException("Recipe operation is not allowlisted.");
            }
        }
    }

    /// <summary>A reference span inside one DAX property, as TE2's dependency tree records it (CR-stripped offsets, inclusive end).</summary>
    public sealed class ReferenceSpan
    {
        public ReferenceSpan(int from, int to, bool fullyQualified) { From = from; To = to; FullyQualified = fullyQualified; }
        public int From { get; }
        public int To { get; }
        public bool FullyQualified { get; }
    }

    /// <summary>One DAX property of an object that mentions the measure being renamed (tracked spans may be empty).</summary>
    public sealed class RenameDependent
    {
        public RenameDependent(string dependentId, string property, string expression, IEnumerable<ReferenceSpan> spans)
        {
            DependentId = dependentId ?? throw new ArgumentNullException(nameof(dependentId));
            Property = property ?? throw new ArgumentNullException(nameof(property));
            Expression = expression ?? "";
            Spans = Array.AsReadOnly((spans ?? Enumerable.Empty<ReferenceSpan>()).OrderBy(s => s.From).ToArray());
        }
        public string DependentId { get; }
        public string Property { get; }
        public string Expression { get; }
        public IReadOnlyList<ReferenceSpan> Spans { get; }
    }

    /// <summary>Host-supplied facts needed to plan a rename. Built fresh from the model for every preview and every apply.</summary>
    public sealed class RenameContext
    {
        public RenameContext(IEnumerable<string> reservedNames, IEnumerable<RenameDependent> dependents, bool autoFixupEnabled)
        {
            ReservedNames = Array.AsReadOnly((reservedNames ?? Enumerable.Empty<string>()).ToArray());
            Dependents = Array.AsReadOnly((dependents ?? Enumerable.Empty<RenameDependent>()).ToArray());
            AutoFixupEnabled = autoFixupEnabled;
        }
        /// <summary>Names the new name must not collide with (case-insensitive), excluding the target itself.</summary>
        public IReadOnlyList<string> ReservedNames { get; }
        public IReadOnlyList<RenameDependent> Dependents { get; }
        public bool AutoFixupEnabled { get; }
    }

    public sealed class MetadataChange
    {
        internal MetadataChange(string id, string property, string before, string after) { Id = id; Property = property; Before = before; After = after; }
        public string Id { get; }
        public string Property { get; }
        public string Before { get; }
        public string After { get; }
    }

    /// <summary>A dependent expression that TE2 FormulaFixup is expected to rewrite when the rename is applied.</summary>
    public sealed class DependentRewrite
    {
        internal DependentRewrite(string dependentId, string property, string before, string after)
        { DependentId = dependentId; Property = property; Before = before; After = after; }
        public string DependentId { get; }
        public string Property { get; }
        public string Before { get; }
        public string After { get; }
    }

    public sealed class MetadataRecipePlan
    {
        internal MetadataRecipePlan(string fingerprint, MetadataRecipeOperation operation, IEnumerable<MetadataChange> changes, IEnumerable<DependentRewrite> rewrites)
        {
            Fingerprint = fingerprint; Operation = operation; Changes = Array.AsReadOnly(changes.ToArray());
            DependentRewrites = Array.AsReadOnly(rewrites.ToArray());
        }
        public string Fingerprint { get; }
        public MetadataRecipeOperation Operation { get; }
        public RecipeDefinition Definition => RecipeCatalog.For(Operation);
        public IReadOnlyList<MetadataChange> Changes { get; }
        public IReadOnlyList<DependentRewrite> DependentRewrites { get; }
    }

    public static class MetadataRecipe
    {
        public const int MaxMeasures = 10000;
        public const int MaxChanges = 1000;
        public const int MaxDependents = 2000;

        public static MetadataRecipePlan Propose(IEnumerable<MeasureMetadata> source, IEnumerable<string> targetIds, MetadataRecipeOperation operation, string value) =>
            Propose(source, targetIds, operation, value, null);

        public static MetadataRecipePlan Propose(IEnumerable<MeasureMetadata> source, IEnumerable<string> targetIds, MetadataRecipeOperation operation, string value, RenameContext? rename)
        {
            if (!Enum.IsDefined(typeof(MetadataRecipeOperation), operation)) throw new ArgumentException("Recipe operation is not allowlisted.");
            var definition = RecipeCatalog.For(operation);
            var invalid = definition.Validate(value);
            if (invalid != null) throw new ArgumentException(invalid);
            value = definition.Normalize(value);
            var snapshot = Snapshot(source);
            var byId = snapshot.ToDictionary(x => x.Id, StringComparer.Ordinal);
            var targets = targetIds.Take(MaxChanges + 1).ToArray();
            if (targets.Length > MaxChanges) throw new ArgumentException("Recipe is limited to 1,000 measures.");
            var distinct = targets.Distinct(StringComparer.Ordinal).ToArray();
            if (definition.SingleTargetOnly && distinct.Length != 1) throw new ArgumentException(definition.Title + " requires exactly one target measure.");
            var changes = new List<MetadataChange>();
            var rewrites = new List<DependentRewrite>();
            foreach (var id in distinct)
            {
                if (!byId.TryGetValue(id, out var item)) throw new ArgumentException("Recipe target is not in the source snapshot.");
                if (operation == MetadataRecipeOperation.SetFormatString && item.HasFormatStringExpression)
                    throw new ArgumentException("A target uses a format string expression; a static format string would be ignored. Edit it in TE2 instead.");
                var before = item.Get(operation);
                if (before == value) continue;
                changes.Add(new MetadataChange(id, definition.PropertyName, before, value));
                if (operation == MetadataRecipeOperation.Rename) rewrites.AddRange(PlanRename(item, value, rename, snapshot));
            }
            return new MetadataRecipePlan(Fingerprint(snapshot, operation == MetadataRecipeOperation.Rename ? rename : null), operation, changes, rewrites);
        }

        public static bool IsCurrent(MetadataRecipePlan plan, IEnumerable<MeasureMetadata> source) => IsCurrent(plan, source, null);

        public static bool IsCurrent(MetadataRecipePlan plan, IEnumerable<MeasureMetadata> source, RenameContext? rename) =>
            plan.Fingerprint == Fingerprint(Snapshot(source), plan.Operation == MetadataRecipeOperation.Rename ? rename : null);

        /// <summary>DAX measure identifier as TE2 writes it: [Name] with ] doubled.</summary>
        public static string MeasureReference(string name) => "[" + (name ?? "").Replace("]", "]]") + "]";

        /// <summary>
        /// Rewrites tracked spans exactly as TE2 FormulaFixup does: carriage returns are removed first (TE2 stores the
        /// fixed-up expression without CR), each span is replaced by the new identifier (measures are never table-qualified
        /// by TE2, so fully-qualified spans also become [New]).
        /// </summary>
        public static string RewriteReferences(string expression, IEnumerable<ReferenceSpan> spans, string newReference)
        {
            var text = (expression ?? "").Replace("\r", "");
            var builder = new StringBuilder();
            var pos = 0;
            foreach (var span in spans.OrderBy(s => s.From))
            {
                if (span.From < pos || span.To < span.From || span.To >= text.Length) throw new ArgumentException("Reference span does not fit the expression.");
                builder.Append(text, pos, span.From - pos).Append(newReference);
                pos = span.To + 1;
            }
            return builder.Append(text, pos, text.Length - pos).ToString();
        }

        /// <summary>Counts textual [Name] occurrences (case-insensitive) in the CR-stripped expression, including inside strings and comments.</summary>
        public static int CountTextualReferences(string expression, string measureName)
        {
            var text = (expression ?? "").Replace("\r", "");
            var needle = MeasureReference(measureName);
            var count = 0;
            for (var i = text.IndexOf(needle, StringComparison.OrdinalIgnoreCase); i >= 0; i = text.IndexOf(needle, i + needle.Length, StringComparison.OrdinalIgnoreCase))
                count++;
            return count;
        }

        private static IEnumerable<DependentRewrite> PlanRename(MeasureMetadata item, string newName, RenameContext? rename, MeasureMetadata[] snapshot)
        {
            if (rename == null) throw new ArgumentException("Rename requires a dependency context from the host.");
            if (!rename.AutoFixupEnabled) throw new ArgumentException("TE2 formula fix-up is off; renaming would leave dependents broken. Enable it in Preferences first.");
            if (item.Name.Length == 0) throw new ArgumentException("Rename target has no name in the snapshot.");
            if (rename.Dependents.Count > MaxDependents) throw new ArgumentException("Rename is limited to 2,000 dependent expressions.");
            var others = snapshot.Where(s => !ReferenceEquals(s, item)).Select(s => s.Name).Concat(rename.ReservedNames);
            if (others.Any(n => string.Equals(n, newName, StringComparison.OrdinalIgnoreCase)) &&
                !string.Equals(item.Name, newName, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Another measure or column already uses this name.");
            var reference = MeasureReference(newName);
            var result = new List<DependentRewrite>();
            foreach (var dependent in rename.Dependents.OrderBy(d => d.DependentId, StringComparer.Ordinal).ThenBy(d => d.Property, StringComparer.Ordinal))
            {
                var textual = CountTextualReferences(dependent.Expression, item.Name);
                if (textual > dependent.Spans.Count)
                    throw new ArgumentException("Refused: " + dependent.DependentId + " (" + dependent.Property + ") mentions " + MeasureReference(item.Name) +
                        " " + textual + " time(s) but TE2 tracks " + dependent.Spans.Count + ". Fix that expression (or the text in strings/comments) first.");
                if (dependent.Spans.Count == 0) continue;
                result.Add(new DependentRewrite(dependent.DependentId, dependent.Property, dependent.Expression,
                    RewriteReferences(dependent.Expression, dependent.Spans, reference)));
            }
            return result;
        }

        private static MeasureMetadata[] Snapshot(IEnumerable<MeasureMetadata> source)
        {
            var items = source.Take(MaxMeasures + 1).ToArray();
            if (items.Length > MaxMeasures || items.Any(x => x == null || string.IsNullOrEmpty(x.Id)) ||
                items.Sum(x => (long)x.Id.Length + x.Name.Length + x.Folder.Length + x.FormatString.Length + x.Description.Length) > 16 * 1024 * 1024 ||
                items.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != items.Length) throw new ArgumentException("Invalid or oversized metadata snapshot.");
            return items.OrderBy(x => x.Id, StringComparer.Ordinal).ToArray();
        }

        private static string Fingerprint(MeasureMetadata[] source, RenameContext? rename)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                {
                    foreach (var item in source)
                    {
                        writer.Write(item.Id); writer.Write(item.Name); writer.Write(item.Folder); writer.Write(item.Hidden);
                        writer.Write(item.FormatString); writer.Write(item.Description); writer.Write(item.HasFormatStringExpression);
                    }
                    if (rename != null)
                    {
                        writer.Write(rename.AutoFixupEnabled);
                        foreach (var name in rename.ReservedNames.OrderBy(n => n, StringComparer.Ordinal)) writer.Write(name);
                        foreach (var d in rename.Dependents.OrderBy(d => d.DependentId, StringComparer.Ordinal).ThenBy(d => d.Property, StringComparer.Ordinal))
                        {
                            writer.Write(d.DependentId); writer.Write(d.Property); writer.Write(d.Expression); writer.Write(d.Spans.Count);
                            foreach (var s in d.Spans) { writer.Write(s.From); writer.Write(s.To); writer.Write(s.FullyQualified); }
                        }
                    }
                }
                using (var sha = SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(stream.ToArray()));
            }
        }
    }
}
