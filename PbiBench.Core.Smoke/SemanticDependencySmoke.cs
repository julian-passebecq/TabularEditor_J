using System;
using System.Linq;
using PbiBench.Core.Semantic;

internal static class SemanticDependencySmoke
{
    public static void Run()
    {
        var graph = new SemanticDependencyGraph(new[]
        {
            new SemanticDependencyEdge
            {
                SourceId = "A",
                TargetId = "B",
                ReferenceCount = 1,
                Properties = new[] { "Expression" }
            },
            new SemanticDependencyEdge
            {
                SourceId = "A",
                TargetId = "B",
                ReferenceCount = 2,
                Properties = new[] { "Format String Expression", "Expression" }
            },
            new SemanticDependencyEdge { SourceId = "B", TargetId = "C" },
            new SemanticDependencyEdge { SourceId = "C", TargetId = "A" },
            new SemanticDependencyEdge { SourceId = "C", TargetId = "D" },
            new SemanticDependencyEdge { SourceId = "", TargetId = "ignored" }
        });

        Assert(graph.Edges.Count == 4, "Semantic graph did not normalize/ignore edges correctly.");

        var direct = graph.GetDependencies("A");
        Assert(direct.Count == 1 && direct[0].TargetId == "B", "Direct semantic dependency lookup failed.");
        Assert(direct[0].ReferenceCount == 3, "Duplicate semantic edges did not merge reference counts.");
        Assert(direct[0].Properties.Length == 2, "Duplicate semantic edges did not merge DAX properties.");

        var reverse = graph.GetDependants("B");
        Assert(reverse.Count == 1 && reverse[0].SourceId == "A", "Reverse semantic dependency lookup failed.");

        var deep = graph.GetDeepDependencies("A");
        Assert(deep.Count == 3, "Cycle-safe deep dependency traversal returned an unexpected count.");
        Assert(deep.All(n => n.ObjectId != "A"), "A cycle returned the traversal start node as its own dependency.");
        Assert(deep.Single(n => n.ObjectId == "B").Depth == 1, "Minimum dependency depth for B is wrong.");
        Assert(deep.Single(n => n.ObjectId == "C").Depth == 2, "Minimum dependency depth for C is wrong.");
        Assert(deep.Single(n => n.ObjectId == "D").Depth == 3, "Minimum dependency depth for D is wrong.");

        var deepReverse = graph.GetDeepDependants("D");
        Assert(deepReverse.Count == 3, "Cycle-safe reverse traversal returned an unexpected count.");
        Assert(deepReverse.Single(n => n.ObjectId == "C").Depth == 1, "Reverse dependency depth for C is wrong.");
        Assert(deepReverse.Single(n => n.ObjectId == "B").Depth == 2, "Reverse dependency depth for B is wrong.");
        Assert(deepReverse.Single(n => n.ObjectId == "A").Depth == 3, "Reverse dependency depth for A is wrong.");

        Assert(graph.GetDependencies("missing").Count == 0, "Unknown source should have no dependencies.");
        Assert(graph.GetDeepDependencies(null).Count == 0, "Null traversal start should return an empty result.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
