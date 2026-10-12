using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Dax;
using PbiBench.Core.Navigation;
using PbiBench.Core.Semantic;
using TabularEditor.PbiBench.Dax;
using TabularEditor.PbiBench.Navigation;
using TabularEditor.TOMWrapper;
using TabularEditor.TOMWrapper.Utils;

namespace TabularEditor.PbiBench.Semantic
{
    /// <summary>
    /// Read-only point-in-time projection of the loaded TE2 model into PbiBench's neutral
    /// navigation and dependency contracts. The snapshot deliberately consumes TE2's existing
    /// dependency engine instead of reparsing DAX in PbiBench.
    /// </summary>
    internal sealed class PbiBenchSemanticSnapshot
    {
        private readonly Dictionary<string, SemanticObjectDescriptor> _descriptorsById;

        private PbiBenchSemanticSnapshot(PbiBenchQuickOpenIndex index, SemanticDependencyGraph graph)
        {
            Index = index;
            Graph = graph;
            _descriptorsById = index.Descriptors.ToDictionary(d => d.Id, StringComparer.Ordinal);
        }

        public PbiBenchQuickOpenIndex Index { get; }
        public SemanticDependencyGraph Graph { get; }
        public SemanticRelationshipDescriptor[] Relationships { get; private set; } = Array.Empty<SemanticRelationshipDescriptor>();
        /// <summary>Fresh model names used for unresolved-reference detection (S2 MODEL-002).</summary>
        public DaxModelNameSet Names { get; private set; } = DaxModelNameSet.Empty;
        /// <summary>Measure referrers, unused-in-model measures and unresolved DAX references.</summary>
        public SemanticUsageIndex Usage { get; private set; } = SemanticUsageIndex.Build(Array.Empty<SemanticUsageObject>(), null);

        public SemanticObjectDescriptor GetDescriptor(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            SemanticObjectDescriptor descriptor;
            return _descriptorsById.TryGetValue(id, out descriptor) ? descriptor : null;
        }

        public static PbiBenchSemanticSnapshot Build(Model model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var index = PbiBenchQuickOpenIndex.Build(model);
            var edges = new List<SemanticDependencyEdge>();

            foreach (var descriptor in index.Descriptors)
            {
                var sourceObject = index.Resolve(descriptor.Id);
                var dependant = sourceObject as IDaxDependantObject;
                if (dependant == null) continue;

                foreach (var dependency in dependant.DependsOn)
                {
                    var targetObject = dependency.Key as ITabularNamedObject;
                    if (targetObject == null || targetObject.IsRemoved) continue;

                    string targetId;
                    if (!index.TryGetId(targetObject, out targetId)) continue;

                    var references = dependency.Value ?? new List<ObjectReference>();
                    edges.Add(new SemanticDependencyEdge
                    {
                        SourceId = descriptor.Id,
                        TargetId = targetId,
                        ReferenceCount = Math.Max(1, references.Count),
                        Properties = references
                            .Select(r => r.property.GetDescription())
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                            .ToArray()
                    });
                }
            }

            var relationships = new List<SemanticRelationshipDescriptor>();
            foreach (var relationship in model.Relationships.OfType<SingleColumnRelationship>())
            {
                index.TryGetId(relationship, out var id);
                var from = relationship.FromColumn;
                var to = relationship.ToColumn;
                relationships.Add(new SemanticRelationshipDescriptor {
                    Id = id ?? "", Name = relationship.Name,
                    From = from?.DaxObjectFullName ?? "Unresolved source",
                    To = to?.DaxObjectFullName ?? "Unresolved target",
                    Cardinality = relationship.FromCardinality + " → " + relationship.ToCardinality,
                    FilterDirection = relationship.CrossFilteringBehavior.ToString(), Active = relationship.IsActive,
                    Unresolved = from == null || to == null || from.IsRemoved || to.IsRemoved
                });
            }
            var graph = new SemanticDependencyGraph(edges);
            var names = PbiBenchDaxModelBinder.Bind(model);
            return new PbiBenchSemanticSnapshot(index, graph) {
                Relationships = relationships.ToArray(), Names = names, Usage = BuildUsage(index, graph, names)
            };
        }

        private static SemanticUsageIndex BuildUsage(PbiBenchQuickOpenIndex index, SemanticDependencyGraph graph, DaxModelNameSet names)
        {
            var objects = index.Descriptors.Select(d => new SemanticUsageObject {
                Id = d.Id, Kind = d.Kind, Name = d.Name, Path = d.EffectivePath, IsHidden = d.IsHidden }).ToArray();
            var unresolved = new List<SemanticUnresolvedReference>();
            foreach (var descriptor in index.Descriptors)
            {
                if (unresolved.Count > SemanticUsageIndex.MaxUnresolved) break;
                var dependant = index.Resolve(descriptor.Id) as IDaxDependantObject;
                if (dependant == null) continue;
                foreach (var property in dependant.GetDAXProperties())
                {
                    var dax = dependant.GetDAX(property);
                    if (string.IsNullOrWhiteSpace(dax)) continue;
                    unresolved.AddRange(SemanticUsageIndex.FindUnresolved(descriptor.Id, descriptor.EffectivePath, property.GetDescription(), dax, names));
                }
            }
            return SemanticUsageIndex.Build(objects, graph, unresolved);
        }
    }
}
