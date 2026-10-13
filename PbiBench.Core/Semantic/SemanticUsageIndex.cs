using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Dax;
using PbiBench.Core.Navigation;

namespace PbiBench.Core.Semantic
{
    public sealed class SemanticUsageObject
    {
        public string Id { get; set; } = "";
        public SemanticObjectKind Kind { get; set; }
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsHidden { get; set; }
    }

    /// <summary>One DAX reference that does not resolve against the fresh model name set.</summary>
    public sealed class SemanticUnresolvedReference
    {
        public string SourceId { get; set; } = "";
        public string SourcePath { get; set; } = "";
        public string Property { get; set; } = "";
        public string Code { get; set; } = "";
        public string Reference { get; set; } = "";
        public string Message { get; set; } = "";
        public int Start { get; set; }
        public int Length { get; set; }
    }

    public sealed class SemanticMeasureUsage
    {
        public SemanticUsageObject Measure { get; set; } = new SemanticUsageObject();
        public IReadOnlyList<SemanticUsageObject> Referrers { get; set; } = Array.Empty<SemanticUsageObject>();
        /// <summary>Uses outside model expressions (e.g. report visuals) when a provider supplied them; null = unknown.</summary>
        public int? ExternalReferences { get; set; }
        public bool IsUnusedInModel => Referrers.Count == 0;
    }

    /// <summary>
    /// Read-only model usage over the neutral dependency graph: measure → referrers, measures no
    /// model expression references, and unresolved references found by advisory diagnostics.
    /// "Unused" only means unused by model expressions; report usage stays unknown until a
    /// report provider supplies external counts.
    /// </summary>
    public sealed class SemanticUsageIndex
    {
        public const int MaxUnresolved = 2000;
        private readonly Dictionary<string, SemanticUsageObject> _objects;
        private readonly SemanticDependencyGraph _graph;

        private SemanticUsageIndex(Dictionary<string, SemanticUsageObject> objects, SemanticDependencyGraph graph,
            IReadOnlyList<SemanticMeasureUsage> measures, IReadOnlyList<SemanticUnresolvedReference> unresolved, bool truncated)
        {
            _objects = objects; _graph = graph; Measures = measures; Unresolved = unresolved; UnresolvedTruncated = truncated;
        }

        public IReadOnlyList<SemanticMeasureUsage> Measures { get; }
        public IReadOnlyList<SemanticUnresolvedReference> Unresolved { get; }
        public bool UnresolvedTruncated { get; }
        public IEnumerable<SemanticMeasureUsage> UnusedMeasures => Measures.Where(m => m.IsUnusedInModel && (m.ExternalReferences ?? 0) == 0);

        public static SemanticUsageIndex Build(IEnumerable<SemanticUsageObject> objects, SemanticDependencyGraph graph,
            IEnumerable<SemanticUnresolvedReference>? unresolved = null, IReadOnlyDictionary<string, int>? externalReferences = null)
        {
            graph ??= new SemanticDependencyGraph(Array.Empty<SemanticDependencyEdge>());
            var map = new Dictionary<string, SemanticUsageObject>(StringComparer.Ordinal);
            foreach (var o in objects ?? Array.Empty<SemanticUsageObject>())
                if (o != null && o.Id.Length > 0 && !map.ContainsKey(o.Id)) map[o.Id] = o;
            var measures = map.Values.Where(o => o.Kind == SemanticObjectKind.Measure)
                .Select(m => new SemanticMeasureUsage {
                    Measure = m,
                    Referrers = graph.GetDependants(m.Id).Select(e => map.TryGetValue(e.SourceId, out var s) ? s : null)
                        .Where(s => s != null && s.Id != m.Id).Select(s => s!)
                        .OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
                    ExternalReferences = externalReferences != null && externalReferences.TryGetValue(m.Id, out var n) ? n : (int?)null
                })
                .OrderBy(m => m.Measure.Path, StringComparer.OrdinalIgnoreCase).ToArray();
            var list = (unresolved ?? Array.Empty<SemanticUnresolvedReference>()).Where(u => u != null)
                .OrderBy(u => u.SourcePath, StringComparer.OrdinalIgnoreCase).ThenBy(u => u.Property, StringComparer.Ordinal).ThenBy(u => u.Start)
                .ToList();
            var truncated = list.Count > MaxUnresolved;
            return new SemanticUsageIndex(map, graph, measures, list.Take(MaxUnresolved).ToArray(), truncated);
        }

        public SemanticUsageObject? Get(string id) => id != null && _objects.TryGetValue(id, out var o) ? o : null;

        public IReadOnlyList<SemanticUsageObject> GetReferrers(string id) =>
            _graph.GetDependants(id).Select(e => Get(e.SourceId)).Where(o => o != null).Select(o => o!).ToArray();

        /// <summary>
        /// Unresolved references in one DAX expression of one model object, using the advisory
        /// diagnostics against a fresh model name set. Delimiter, arity and unknown-function findings are excluded.
        /// </summary>
        public static IReadOnlyList<SemanticUnresolvedReference> FindUnresolved(string sourceId, string sourcePath, string property,
            string expression, DaxModelNameSet names)
        {
            if (string.IsNullOrWhiteSpace(expression) || names == null || !names.HasModel) return Array.Empty<SemanticUnresolvedReference>();
            if (expression.Length > DaxAuthoring.MaxTextLength) return Array.Empty<SemanticUnresolvedReference>();
            return DaxDiagnostics.Analyze(expression, names)
                // Unknown-function hints are excluded here: model expressions were accepted by an engine
                // that may know functions newer than the pinned vocabulary (e.g. NAMEOF in field parameters).
                .Where(d => DaxDiagnosticCodes.IsUnresolvedReference(d.Code) && d.Code != DaxDiagnosticCodes.UnknownFunction)
                .Select(d => new SemanticUnresolvedReference {
                    SourceId = sourceId ?? "", SourcePath = sourcePath ?? "", Property = property ?? "", Code = d.Code,
                    Reference = d.Reference, Message = d.Message, Start = d.Start, Length = d.Length
                }).ToArray();
        }
    }
}
