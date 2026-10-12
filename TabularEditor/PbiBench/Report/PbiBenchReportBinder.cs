using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PbiBench.Core.Dax;
using PbiBench.Core.Report;
using TabularEditor.PbiBench.Dax;
using TabularEditor.PbiBench.Navigation;
using TabularEditor.PbiBench.Project;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Report
{
    /// <summary>Host side of report lineage: fresh model names + hierarchies, object resolution, report discovery.</summary>
    internal static class PbiBenchReportBinder
    {
        internal static ReportLineage BuildLineage(PbirReportIndex index, Model model)
        {
            if (model == null) return ReportLineage.Build(index, DaxModelNameSet.Empty);
            return ReportLineage.Build(index, PbiBenchDaxModelBinder.Bind(model), Hierarchies(model));
        }

        internal static IReadOnlyDictionary<string, string[]> Hierarchies(Model model)
        {
            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in model.Tables) result[table.Name] = table.Hierarchies.Select(h => h.Name).ToArray();
            return result;
        }

        /// <summary>Live model object for a resolved or ambiguous entry (the object the report name points at), or null.</summary>
        internal static ITabularNamedObject Resolve(Model model, ReportLineageEntry entry)
        {
            if (model == null || entry == null || entry.ModelTable == null || entry.ModelName == null) return null;
            var table = model.Tables.FirstOrDefault(t => string.Equals(t.Name, entry.ModelTable, StringComparison.OrdinalIgnoreCase));
            if (table == null) return null;
            bool Same(string name) => string.Equals(name, entry.ModelName, StringComparison.OrdinalIgnoreCase);
            if (entry.Kind == PbirFieldKind.Hierarchy) return table.Hierarchies.FirstOrDefault(h => Same(h.Name));
            return (ITabularNamedObject)table.Measures.FirstOrDefault(m => Same(m.Name)) ?? table.Columns.FirstOrDefault(c => Same(c.Name));
        }

        /// <summary>X.Report folders next to the loaded model's X.SemanticModel folder (PBIP layout), or empty.</summary>
        internal static IReadOnlyList<string> FindReports(TabularModelHandler handler)
        {
            var target = handler == null ? null : PbiBenchSaveTargetTracker.TargetOf(handler.SourceType, handler.Source);
            var folder = target == null ? null : (Directory.Exists(target) ? target : Path.GetDirectoryName(target));
            for (var depth = 0; folder != null && depth < 3; depth++, folder = Path.GetDirectoryName(folder))
                if (folder.EndsWith(".SemanticModel", StringComparison.OrdinalIgnoreCase)) return PbirReportIndex.FindReportsFor(folder);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// "Used in report" counts for the S2 Usage tab: the last report lineage computed in this session against
    /// the loaded model. Counts are only handed out while the model name revision still matches.
    /// </summary>
    internal static class PbiBenchReportUsage
    {
        private static readonly object Gate = new object();
        private static ReportLineage _lineage;
        private static string _label;

        internal static void Attach(ReportLineage lineage, string label)
        {
            lock (Gate) { _lineage = lineage; _label = label; }
        }

        internal static void Clear() { lock (Gate) { _lineage = null; _label = null; } }

        internal static string Label { get { lock (Gate) return _label; } }

        /// <summary>Measure id → report uses for every measure, or null when no current report lineage matches these names.</summary>
        internal static IReadOnlyDictionary<string, int> GetMeasureCounts(PbiBenchQuickOpenIndex index, DaxModelNameSet names)
        {
            ReportLineage lineage;
            lock (Gate) lineage = _lineage;
            if (lineage == null || index == null || names == null || lineage.ModelRevision != names.Revision) return null;
            var uses = lineage.MeasureUses();
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var descriptor in index.Descriptors)
                if (index.Resolve(descriptor.Id) is Measure measure)
                    result[descriptor.Id] = uses.TryGetValue(ReportLineage.MeasureKey(measure.Table.Name, measure.Name), out var n) ? n : 0;
            return result;
        }
    }
}
