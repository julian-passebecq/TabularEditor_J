using System;
using System.Collections.Generic;

namespace PbiBench.Core.Report
{
    public enum PbirFieldKind { Column, Measure, Hierarchy, HierarchyLevel }

    /// <summary>Where a field is used: projection role, visual/page/report filter, sort or formatting.</summary>
    public enum PbirFieldUsage { Projection, VisualFilter, PageFilter, ReportFilter, Sort, Formatting, Other }

    /// <summary>One model field reference found in PBIR JSON (Entity = table name as written by Power BI).</summary>
    public sealed class PbirFieldReference
    {
        public PbirFieldKind Kind { get; set; }
        /// <summary>Table name, or null when the expression uses a query alias (SourceRef.Source) instead of an entity.</summary>
        public string? Entity { get; set; }
        public string? SourceAlias { get; set; }
        /// <summary>Column/measure name; for hierarchies the hierarchy name.</summary>
        public string Property { get; set; } = "";
        /// <summary>Level name for HierarchyLevel references.</summary>
        public string? Level { get; set; }
        /// <summary>True for auto date/time variations (PropertyVariationSource): Property is then the base column.</summary>
        public bool IsVariation { get; set; }
        public PbirFieldUsage Usage { get; set; }
        /// <summary>Projection role (Values, Category, ...) or filter name.</summary>
        public string Role { get; set; } = "";
        public string Page { get; set; } = "";
        public string Visual { get; set; } = "";
        public string File { get; set; } = "";

        /// <summary>Model-facing key used for grouping: kind|table|name (hierarchy level folds into its hierarchy).</summary>
        public string Key => (Kind == PbirFieldKind.HierarchyLevel ? PbirFieldKind.Hierarchy : Kind) + "|" + (Entity ?? "@" + SourceAlias) + "|" + Property;

        public string Display =>
            (Entity != null ? "'" + Entity.Replace("'", "''") + "'" : "<alias " + SourceAlias + ">") +
            (Kind == PbirFieldKind.Hierarchy || Kind == PbirFieldKind.HierarchyLevel ? " hierarchy " + Property + (Level != null ? " / " + Level : "")
                : "[" + Property.Replace("]", "]]") + "]");
    }

    /// <summary>
    /// Finds field expressions anywhere under a JSON subtree. PBIR field expressions have the shape
    /// {"Column"|"Measure"|"Hierarchy"|"HierarchyLevel": {"Expression": {"SourceRef": {"Entity": t}}, "Property": n}};
    /// Aggregation wraps a Column and is reported as that column.
    /// </summary>
    public static class PbirFieldExtractor
    {
        public static void Extract(PbirJsonNode node, PbirFieldUsage usage, string role, Action<PbirFieldReference> add, int maxDepth = 64)
        {
            Walk(node, usage, role, add, 0, maxDepth);
        }

        private static void Walk(PbirJsonNode node, PbirFieldUsage usage, string role, Action<PbirFieldReference> add, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            if (node.IsArray) { foreach (var item in node.Items) Walk(item, usage, role, add, depth + 1, maxDepth); return; }
            if (!node.IsObject) return;
            foreach (var p in node.Properties)
            {
                if (p.Value.IsObject)
                {
                    var reference = TryRead(p.Key, p.Value);
                    if (reference != null)
                    {
                        reference.Usage = usage; reference.Role = role;
                        add(reference);
                        continue; // the inner expression of a matched field is not another field
                    }
                }
                Walk(p.Value, usage, role, add, depth + 1, maxDepth);
            }
        }

        internal static PbirFieldReference? TryRead(string key, PbirJsonNode body)
        {
            switch (key)
            {
                case "Column":
                case "Measure":
                    {
                        var property = body.GetString("Property");
                        if (property == null) return null;
                        var expression = body.Get("Expression");
                        if (expression == null) return null;
                        var reference = new PbirFieldReference { Kind = key == "Column" ? PbirFieldKind.Column : PbirFieldKind.Measure, Property = property };
                        if (!ReadSource(expression, reference)) return null;
                        return reference;
                    }
                case "Hierarchy":
                    {
                        var hierarchy = body.GetString("Hierarchy");
                        var expression = body.Get("Expression");
                        if (hierarchy == null || expression == null) return null;
                        var variation = expression.Get("PropertyVariationSource");
                        if (variation != null)
                        {
                            // Auto date/time: the hierarchy lives on a hidden local date table; report the base column.
                            var baseColumn = variation.GetString("Property");
                            var inner = variation.Get("Expression");
                            if (baseColumn == null || inner == null) return null;
                            var column = new PbirFieldReference { Kind = PbirFieldKind.Column, Property = baseColumn, IsVariation = true };
                            return ReadSource(inner, column) ? column : null;
                        }
                        var reference = new PbirFieldReference { Kind = PbirFieldKind.Hierarchy, Property = hierarchy };
                        return ReadSource(expression, reference) ? reference : null;
                    }
                case "HierarchyLevel":
                    {
                        var level = body.GetString("Level");
                        var inner = body.Path("Expression", "Hierarchy");
                        if (level == null || inner == null) return null;
                        var hierarchy = TryRead("Hierarchy", inner);
                        if (hierarchy == null) return null;
                        if (hierarchy.IsVariation) return hierarchy;
                        hierarchy.Kind = PbirFieldKind.HierarchyLevel;
                        hierarchy.Level = level;
                        return hierarchy;
                    }
                default:
                    return null;
            }
        }

        private static bool ReadSource(PbirJsonNode expression, PbirFieldReference reference)
        {
            var source = expression.Get("SourceRef");
            if (source == null) return false;
            var entity = source.GetString("Entity");
            var alias = source.GetString("Source");
            if (entity == null && alias == null) return false;
            reference.Entity = entity;
            reference.SourceAlias = entity == null ? alias : null;
            return true;
        }
    }
}
