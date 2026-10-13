using System;

namespace PbiBench.Core.Navigation
{
    /// <summary>
    /// Provider-neutral semantic object kinds used by PbiBench navigation features.
    /// Keep this independent from TOM so the search/ranking engine is easy to test and
    /// can later be fed from loaded models, TMDL on disk, or other read-only indexes.
    /// </summary>
    public enum SemanticObjectKind
    {
        Model = 0,
        Table = 1,
        Measure = 2,
        Column = 3,
        Hierarchy = 4,
        Level = 5,
        Relationship = 6,
        CalculationGroup = 7,
        CalculationItem = 8,
        Function = 9,
        Perspective = 10,
        Role = 11,
        Partition = 12,
        DataSource = 13,
        Other = 99
    }

    /// <summary>
    /// Small immutable-by-convention projection of a semantic-model object for Quick Open.
    /// The UI/TOM adapter owns object creation; the neutral core only searches these values.
    /// </summary>
    public sealed class SemanticObjectDescriptor
    {
        private string[] _searchTerms = Array.Empty<string>();

        public string Id { get; set; } = string.Empty;
        public SemanticObjectKind Kind { get; set; } = SemanticObjectKind.Other;
        public string Name { get; set; } = string.Empty;
        public string ParentName { get; set; } = string.Empty;
        public string DisplayPath { get; set; } = string.Empty;
        public bool IsHidden { get; set; }

        public string[] SearchTerms
        {
            get => _searchTerms;
            set => _searchTerms = value ?? Array.Empty<string>();
        }

        public string EffectivePath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(DisplayPath)) return DisplayPath;
                if (!string.IsNullOrWhiteSpace(ParentName)) return ParentName + " / " + Name;
                return Name;
            }
        }
    }
}
