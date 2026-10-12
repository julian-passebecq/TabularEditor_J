using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Dax;

namespace PbiBench.Core.Report
{
    public enum ReportLineageState { Resolved, Unresolved, Ambiguous }

    /// <summary>One distinct report field (grouped by kind/table/name) and how it maps to the model.</summary>
    public sealed class ReportLineageEntry
    {
        public PbirFieldKind Kind { get; set; }
        public string? Table { get; set; }
        public string Name { get; set; } = "";
        public string Display { get; set; } = "";
        public ReportLineageState State { get; set; }
        public string Reason { get; set; } = "";
        /// <summary>Model table/name spelling when resolved (case can differ from the report).</summary>
        public string? ModelTable { get; set; }
        public string? ModelName { get; set; }
        public int Uses { get; set; }
        public int Visuals { get; set; }
        public IReadOnlyList<PbirFieldReference> Locations { get; set; } = Array.Empty<PbirFieldReference>();
        public string FirstLocation => Locations.Count == 0 ? "" : Describe(Locations[0]);

        public static string Describe(PbirFieldReference r) =>
            r.Usage == PbirFieldUsage.ReportFilter ? "report filter " + r.Role
            : r.Usage == PbirFieldUsage.PageFilter ? r.Page + " · page filter " + r.Role
            : r.Page + " · " + r.Visual + " · " + (r.Usage == PbirFieldUsage.Projection ? r.Role : r.Usage + (r.Role.Length > 0 ? " " + r.Role : ""));
    }

    /// <summary>
    /// Report → model lineage against a fresh model name set. States:
    /// Resolved = the table and an object of the referenced kind exist (case-insensitive, like the engine).
    /// Unresolved = the table or the object is missing (a measure living on another table is reported here with a hint).
    /// Ambiguous = cannot be decided from names alone: a query alias instead of a table, a column/measure kind
    /// mismatch, or a hierarchy when no hierarchy names were supplied.
    /// ModelRevision binds the result to the name set; consumers must discard it once the revision changes.
    /// </summary>
    public sealed class ReportLineage
    {
        public const int MaxLocationsPerEntry = 50;

        public IReadOnlyList<ReportLineageEntry> Entries { get; private set; } = Array.Empty<ReportLineageEntry>();
        public string ModelRevision { get; private set; } = "";
        public string ReportFolder { get; private set; } = "";
        public int Count(ReportLineageState state) => Entries.Count(e => e.State == state);

        /// <param name="hierarchies">Optional table → hierarchy names; null leaves hierarchy references Ambiguous.</param>
        public static ReportLineage Build(PbirReportIndex index, DaxModelNameSet names, IReadOnlyDictionary<string, string[]>? hierarchies = null)
        {
            if (index == null) throw new ArgumentNullException(nameof(index));
            names ??= DaxModelNameSet.Empty;
            var entries = new List<ReportLineageEntry>();
            foreach (var group in index.AllFields.GroupBy(f => f.Key, StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                var entry = new ReportLineageEntry {
                    Kind = first.Kind == PbirFieldKind.HierarchyLevel ? PbirFieldKind.Hierarchy : first.Kind,
                    Table = first.Entity, Name = first.Property,
                    Display = first.Kind == PbirFieldKind.HierarchyLevel ? new PbirFieldReference { Kind = PbirFieldKind.Hierarchy, Entity = first.Entity, SourceAlias = first.SourceAlias, Property = first.Property }.Display : first.Display,
                    Uses = group.Count(),
                    Visuals = group.Where(g => g.Visual.Length > 0).Select(g => g.Page + "\u0001" + g.Visual).Distinct().Count(),
                    Locations = group.Take(MaxLocationsPerEntry).ToArray()
                };
                Resolve(entry, first, names, hierarchies);
                entries.Add(entry);
            }
            return new ReportLineage {
                Entries = entries.OrderBy(e => e.State == ReportLineageState.Resolved ? 2 : e.State == ReportLineageState.Ambiguous ? 1 : 0)
                    .ThenBy(e => e.Table ?? "", StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray(),
                ModelRevision = names.Revision, ReportFolder = index.ReportFolder
            };
        }

        private static void Resolve(ReportLineageEntry entry, PbirFieldReference reference, DaxModelNameSet names, IReadOnlyDictionary<string, string[]>? hierarchies)
        {
            if (reference.Entity == null)
            { entry.State = ReportLineageState.Ambiguous; entry.Reason = "Uses query alias '" + reference.SourceAlias + "' instead of a table name; not resolved."; return; }
            var table = names.GetTable(reference.Entity);
            if (table == null)
            { entry.State = ReportLineageState.Unresolved; entry.Reason = "Table '" + reference.Entity + "' is not in the model."; return; }
            entry.ModelTable = table.Name;
            string? Find(IEnumerable<string> list) => list.FirstOrDefault(n => string.Equals(n, reference.Property, StringComparison.OrdinalIgnoreCase));
            var column = Find(table.Columns);
            var measure = Find(table.Measures);
            switch (entry.Kind)
            {
                case PbirFieldKind.Measure:
                    if (measure != null) { Resolved(entry, measure); return; }
                    if (column != null) { Ambiguous(entry, column, "Report expects a measure; the model has a column of that name on '" + table.Name + "'."); return; }
                    var home = names.Tables.FirstOrDefault(t => t.Measures.Any(m => string.Equals(m, reference.Property, StringComparison.OrdinalIgnoreCase)));
                    entry.State = ReportLineageState.Unresolved;
                    entry.Reason = home != null ? "Measure exists on table '" + home.Name + "', not on '" + table.Name + "'." : "Measure is not in the model.";
                    return;
                case PbirFieldKind.Column:
                    if (column != null) { Resolved(entry, column); if (reference.IsVariation) entry.Reason = "Auto date/time hierarchy over this column."; return; }
                    if (measure != null) { Ambiguous(entry, measure, "Report expects a column; the model has a measure of that name on '" + table.Name + "'."); return; }
                    entry.State = ReportLineageState.Unresolved; entry.Reason = "Column is not on table '" + table.Name + "'.";
                    return;
                default:
                    if (hierarchies == null) { entry.State = ReportLineageState.Ambiguous; entry.Reason = "Table exists; hierarchy names were not supplied, so the hierarchy is not checked."; return; }
                    var hierarchy = hierarchies.TryGetValue(table.Name, out var list) ? Find(list) : null;
                    if (hierarchy != null) { Resolved(entry, hierarchy); entry.Reason = "Hierarchy found (levels not checked)."; return; }
                    entry.State = ReportLineageState.Unresolved; entry.Reason = "Hierarchy is not on table '" + table.Name + "'.";
                    return;
            }
        }

        private static void Resolved(ReportLineageEntry entry, string modelName) { entry.State = ReportLineageState.Resolved; entry.ModelName = modelName; entry.Reason = ""; }
        private static void Ambiguous(ReportLineageEntry entry, string modelName, string reason) { entry.State = ReportLineageState.Ambiguous; entry.ModelName = modelName; entry.Reason = reason; }

        /// <summary>Report uses per resolved model measure, keyed by <see cref="MeasureKey"/> (for the Usage tab).</summary>
        public IReadOnlyDictionary<string, int> MeasureUses()
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in Entries)
                if (e.State == ReportLineageState.Resolved && e.Kind == PbirFieldKind.Measure && e.ModelTable != null && e.ModelName != null)
                {
                    var key = MeasureKey(e.ModelTable, e.ModelName);
                    result[key] = (result.TryGetValue(key, out var n) ? n : 0) + e.Uses;
                }
            return result;
        }

        public static string MeasureKey(string table, string measure) => table + "\u0001" + measure;
    }
}
