using System;
using System.Collections.Generic;
using System.Linq;

namespace PbiBench.Core.Semantic
{
    /// <summary>
    /// Directed semantic dependency edge. Source is the dependant expression/object and Target
    /// is the semantic object referenced by that expression. ReferenceCount captures how many
    /// references were observed for the source-target pair; Properties identifies the DAX
    /// properties that produced those references.
    /// </summary>
    public sealed class SemanticDependencyEdge
    {
        public string SourceId { get; set; } = string.Empty;
        public string TargetId { get; set; } = string.Empty;
        public int ReferenceCount { get; set; } = 1;
        public string[] Properties { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Result of a transitive dependency traversal. Depth is the minimum number of dependency
    /// edges between the requested start object and ObjectId.
    /// </summary>
    public sealed class SemanticDependencyNode
    {
        public SemanticDependencyNode(string objectId, int depth)
        {
            ObjectId = objectId ?? string.Empty;
            Depth = depth;
        }

        public string ObjectId { get; }
        public int Depth { get; }
    }

    /// <summary>
    /// Provider-neutral, read-only dependency graph used by Semantic View and future PBIR/Git
    /// impact analysis. Input edges are normalized so callers can safely combine dependency
    /// observations from multiple providers without duplicating source-target relationships.
    /// </summary>
    public sealed class SemanticDependencyGraph
    {
        private readonly IReadOnlyList<SemanticDependencyEdge> _edges;
        private readonly Dictionary<string, SemanticDependencyEdge[]> _dependencies;
        private readonly Dictionary<string, SemanticDependencyEdge[]> _dependants;

        public SemanticDependencyGraph(IEnumerable<SemanticDependencyEdge> edges)
        {
            _edges = Normalize(edges ?? Array.Empty<SemanticDependencyEdge>());
            _dependencies = _edges
                .GroupBy(e => e.SourceId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(e => e.TargetId, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);
            _dependants = _edges
                .GroupBy(e => e.TargetId, StringComparer.Ordinal)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(e => e.SourceId, StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);
        }

        public IReadOnlyList<SemanticDependencyEdge> Edges => _edges;

        public IReadOnlyList<SemanticDependencyEdge> GetDependencies(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) return Array.Empty<SemanticDependencyEdge>();
            SemanticDependencyEdge[] result;
            return _dependencies.TryGetValue(sourceId, out result)
                ? result
                : (IReadOnlyList<SemanticDependencyEdge>)Array.Empty<SemanticDependencyEdge>();
        }

        public IReadOnlyList<SemanticDependencyEdge> GetDependants(string targetId)
        {
            if (string.IsNullOrWhiteSpace(targetId)) return Array.Empty<SemanticDependencyEdge>();
            SemanticDependencyEdge[] result;
            return _dependants.TryGetValue(targetId, out result)
                ? result
                : (IReadOnlyList<SemanticDependencyEdge>)Array.Empty<SemanticDependencyEdge>();
        }

        public IReadOnlyList<SemanticDependencyNode> GetDeepDependencies(string sourceId)
        {
            return Traverse(sourceId, true);
        }

        public IReadOnlyList<SemanticDependencyNode> GetDeepDependants(string targetId)
        {
            return Traverse(targetId, false);
        }

        private IReadOnlyList<SemanticDependencyNode> Traverse(string startId, bool forward)
        {
            if (string.IsNullOrWhiteSpace(startId)) return Array.Empty<SemanticDependencyNode>();

            // Seed the start node as visited so a cycle can never return the start object as one
            // of its own transitive dependencies/dependants.
            var visited = new HashSet<string>(StringComparer.Ordinal) { startId };
            var queue = new Queue<SemanticDependencyNode>();
            var results = new List<SemanticDependencyNode>();

            EnqueueNeighbours(startId, 1, forward, visited, queue);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                results.Add(current);
                EnqueueNeighbours(current.ObjectId, current.Depth + 1, forward, visited, queue);
            }

            return results
                .OrderBy(n => n.Depth)
                .ThenBy(n => n.ObjectId, StringComparer.Ordinal)
                .ToArray();
        }

        private void EnqueueNeighbours(
            string objectId,
            int depth,
            bool forward,
            HashSet<string> visited,
            Queue<SemanticDependencyNode> queue)
        {
            var edges = forward ? GetDependencies(objectId) : GetDependants(objectId);
            foreach (var edge in edges)
            {
                var nextId = forward ? edge.TargetId : edge.SourceId;
                if (string.IsNullOrWhiteSpace(nextId) || !visited.Add(nextId)) continue;
                queue.Enqueue(new SemanticDependencyNode(nextId, depth));
            }
        }

        private static IReadOnlyList<SemanticDependencyEdge> Normalize(IEnumerable<SemanticDependencyEdge> edges)
        {
            var normalized = edges
                .Where(e => e != null)
                .Where(e => !string.IsNullOrWhiteSpace(e.SourceId) && !string.IsNullOrWhiteSpace(e.TargetId))
                .GroupBy(e => new EdgeKey(e.SourceId.Trim(), e.TargetId.Trim()))
                .Select(g => new SemanticDependencyEdge
                {
                    SourceId = g.Key.SourceId,
                    TargetId = g.Key.TargetId,
                    ReferenceCount = g.Sum(e => Math.Max(1, e.ReferenceCount)),
                    Properties = g
                        .SelectMany(e => e.Properties ?? Array.Empty<string>())
                        .Where(p => !string.IsNullOrWhiteSpace(p))
                        .Select(p => p.Trim())
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                })
                .OrderBy(e => e.SourceId, StringComparer.Ordinal)
                .ThenBy(e => e.TargetId, StringComparer.Ordinal)
                .ToArray();

            return normalized;
        }

        private struct EdgeKey : IEquatable<EdgeKey>
        {
            public EdgeKey(string sourceId, string targetId)
            {
                SourceId = sourceId;
                TargetId = targetId;
            }

            public string SourceId { get; }
            public string TargetId { get; }

            public bool Equals(EdgeKey other)
            {
                return string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
                    && string.Equals(TargetId, other.TargetId, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is EdgeKey && Equals((EdgeKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((SourceId != null ? StringComparer.Ordinal.GetHashCode(SourceId) : 0) * 397)
                        ^ (TargetId != null ? StringComparer.Ordinal.GetHashCode(TargetId) : 0);
                }
            }
        }
    }
}
