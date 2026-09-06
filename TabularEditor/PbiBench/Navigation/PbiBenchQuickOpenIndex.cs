using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PbiBench.Core.Navigation;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Navigation
{
    /// <summary>
    /// Read-only adapter from the loaded TE2 TOMWrapper graph into the provider-neutral
    /// PbiBench Quick Open index. No UI tree state is consulted and no model mutation occurs.
    /// </summary>
    internal sealed class PbiBenchQuickOpenIndex
    {
        private readonly Dictionary<string, ITabularNamedObject> _objectsById;

        private PbiBenchQuickOpenIndex(
            IReadOnlyList<SemanticObjectDescriptor> descriptors,
            Dictionary<string, ITabularNamedObject> objectsById)
        {
            Descriptors = descriptors;
            _objectsById = objectsById;
        }

        public IReadOnlyList<SemanticObjectDescriptor> Descriptors { get; }

        public ITabularNamedObject Resolve(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            ITabularNamedObject result;
            return _objectsById.TryGetValue(id, out result) ? result : null;
        }

        public static PbiBenchQuickOpenIndex Build(Model model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));

            var descriptors = new List<SemanticObjectDescriptor>();
            var objectsById = new Dictionary<string, ITabularNamedObject>(StringComparer.Ordinal);
            var seen = new HashSet<ITabularNamedObject>();
            var sequence = 0;

            Visit(model, null, string.Empty, descriptors, objectsById, seen, ref sequence);

            return new PbiBenchQuickOpenIndex(descriptors, objectsById);
        }

        private static void Visit(
            ITabularNamedObject current,
            ITabularNamedObject parent,
            string parentPath,
            List<SemanticObjectDescriptor> descriptors,
            Dictionary<string, ITabularNamedObject> objectsById,
            HashSet<ITabularNamedObject> seen,
            ref int sequence)
        {
            if (current == null || current.IsRemoved || !seen.Add(current)) return;

            var traversalPath = AppendPath(parentPath, current.Name);
            var kind = MapKind(current.ObjectType);

            // Keep virtual tree/group helper objects out of the neutral semantic index, while
            // still traversing through containers so their real semantic children are indexed.
            if (kind != SemanticObjectKind.Other)
            {
                sequence++;
                var id = "tom:" + sequence.ToString(CultureInfo.InvariantCulture);
                var descriptor = new SemanticObjectDescriptor
                {
                    Id = id,
                    Kind = kind,
                    Name = current.Name ?? string.Empty,
                    ParentName = GetSemanticParentName(current, parent),
                    DisplayPath = GetDisplayPath(current, traversalPath),
                    IsHidden = (current as IHideableObject)?.IsHidden ?? false,
                    SearchTerms = GetSearchTerms(current)
                };

                descriptors.Add(descriptor);
                objectsById[id] = current;
            }

            var container = current as ITabularObjectContainer;
            if (container == null) return;

            var children = container.GetChildren();
            if (children == null) return;

            foreach (var child in children)
                Visit(child, current, traversalPath, descriptors, objectsById, seen, ref sequence);
        }

        private static string GetSemanticParentName(ITabularNamedObject current, ITabularNamedObject parent)
        {
            var tableObject = current as ITabularTableObject;
            if (tableObject?.Table != null && !ReferenceEquals(tableObject.Table, current))
                return tableObject.Table.Name ?? string.Empty;

            return parent?.Name ?? string.Empty;
        }

        private static string GetDisplayPath(ITabularNamedObject current, string traversalPath)
        {
            var daxObject = current as IDaxObject;
            if (!string.IsNullOrWhiteSpace(daxObject?.DaxObjectFullName))
                return daxObject.DaxObjectFullName;

            var tableObject = current as ITabularTableObject;
            if (tableObject?.Table != null)
            {
                var path = tableObject.Table.Name ?? string.Empty;
                var folderObject = current as IFolderObject;
                if (!string.IsNullOrWhiteSpace(folderObject?.DisplayFolder))
                    path = AppendPath(path, folderObject.DisplayFolder.Replace("\\", " / "));
                return AppendPath(path, current.Name);
            }

            return traversalPath;
        }

        private static string[] GetSearchTerms(ITabularNamedObject current)
        {
            var terms = new List<string>();

            var daxObject = current as IDaxObject;
            if (daxObject != null)
            {
                AddTerm(terms, daxObject.DaxObjectName);
                AddTerm(terms, daxObject.DaxObjectFullName);
                AddTerm(terms, daxObject.DaxTableName);
            }

            var tableObject = current as ITabularTableObject;
            AddTerm(terms, tableObject?.Table?.Name);

            var folderObject = current as IFolderObject;
            AddTerm(terms, folderObject?.DisplayFolder);

            AddTerm(terms, current.ObjectType.ToString());

            return terms
                .Where(t => !string.Equals(t, current.Name, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static void AddTerm(List<string> terms, string value)
        {
            if (!string.IsNullOrWhiteSpace(value)) terms.Add(value.Trim());
        }

        private static string AppendPath(string parent, string child)
        {
            if (string.IsNullOrWhiteSpace(parent)) return child ?? string.Empty;
            if (string.IsNullOrWhiteSpace(child)) return parent;
            return parent + " / " + child;
        }

        private static SemanticObjectKind MapKind(ObjectType objectType)
        {
            switch (objectType)
            {
                case ObjectType.Model: return SemanticObjectKind.Model;
                case ObjectType.Table: return SemanticObjectKind.Table;
                case ObjectType.Measure: return SemanticObjectKind.Measure;
                case ObjectType.Column: return SemanticObjectKind.Column;
                case ObjectType.Hierarchy: return SemanticObjectKind.Hierarchy;
                case ObjectType.Level: return SemanticObjectKind.Level;
                case ObjectType.Relationship: return SemanticObjectKind.Relationship;
                case ObjectType.CalculationGroupTable:
                case ObjectType.CalculationGroup:
                    return SemanticObjectKind.CalculationGroup;
                case ObjectType.CalculationItem: return SemanticObjectKind.CalculationItem;
                case ObjectType.Function: return SemanticObjectKind.Function;
                case ObjectType.Perspective: return SemanticObjectKind.Perspective;
                case ObjectType.Role: return SemanticObjectKind.Role;
                case ObjectType.Partition: return SemanticObjectKind.Partition;
                case ObjectType.DataSource: return SemanticObjectKind.DataSource;
                default: return SemanticObjectKind.Other;
            }
        }
    }
}
