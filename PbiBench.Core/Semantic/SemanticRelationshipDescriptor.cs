namespace PbiBench.Core.Semantic
{
    /// <summary>Read-only evidence from the loaded model, never a mutable semantic owner.</summary>
    public sealed class SemanticRelationshipDescriptor
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string Cardinality { get; set; } = "";
        public string FilterDirection { get; set; } = "";
        public bool Active { get; set; }
        public bool Unresolved { get; set; }
    }
}
