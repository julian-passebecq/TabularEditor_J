using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Navigation;
using PbiBench.Core.Semantic;
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

            return new PbiBenchSemanticSnapshot(index, new SemanticDependencyGraph(edges));
        }
    }
}
