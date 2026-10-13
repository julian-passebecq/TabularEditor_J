using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Automation;
using TabularEditor.TOMWrapper;
using TabularEditor.TOMWrapper.Utils;

namespace TabularEditor.PbiBench.Automation
{
    /// <summary>
    /// Host adapter for reviewed measure recipes (AUTO-002): snapshots the TOM measures into the neutral core,
    /// re-checks freshness and object identity at apply, applies in exactly one TE2 undo batch and verifies every
    /// value (including FormulaFixup rewrites for rename) before committing; any mismatch rolls the batch back.
    /// </summary>
    internal sealed class PbiBenchMetadataRecipe
    {
        private readonly TabularModelHandler _handler;
        private readonly Dictionary<string, Measure> _objects;
        internal MetadataRecipePlan Plan { get; }

        internal PbiBenchMetadataRecipe(TabularModelHandler handler, IEnumerable<Measure> targets, MetadataRecipeOperation operation, string value)
        {
            _handler = handler;
            _objects = handler.Model.AllMeasures.Take(MetadataRecipe.MaxMeasures + 1).ToDictionary(m => m.DaxObjectFullName, StringComparer.Ordinal);
            var targetList = targets.ToList();
            var context = operation == MetadataRecipeOperation.Rename && targetList.Count == 1 ? BuildRenameContext(handler, targetList[0]) : null;
            Plan = MetadataRecipe.Propose(Snapshot(), targetList.Select(m => m.DaxObjectFullName), operation, value, context);
        }

        private IEnumerable<MeasureMetadata> Snapshot() => _handler.Model.AllMeasures.Select(m => new MeasureMetadata(m.DaxObjectFullName, m.Name,
            m.DisplayFolder, m.IsHidden, m.FormatString, m.Description, !string.IsNullOrEmpty(SafeFormatStringExpression(m))));

        private static string SafeFormatStringExpression(Measure m)
        {
            try { return m.FormatStringExpression; } catch (Exception) { return null; } // not available below compatibility 1601
        }

        internal static string Read(Measure measure, MetadataRecipeOperation operation)
        {
            switch (operation)
            {
                case MetadataRecipeOperation.SetDisplayFolder: return measure.DisplayFolder ?? "";
                case MetadataRecipeOperation.SetHidden: return measure.IsHidden.ToString();
                case MetadataRecipeOperation.SetFormatString: return measure.FormatString ?? "";
                case MetadataRecipeOperation.SetDescription: return measure.Description ?? "";
                case MetadataRecipeOperation.Rename: return measure.Name;
                default: throw new InvalidOperationException("Recipe operation is not allowlisted.");
            }
        }

        private static void Write(Measure measure, MetadataRecipeOperation operation, string value)
        {
            switch (operation)
            {
                case MetadataRecipeOperation.SetDisplayFolder: measure.DisplayFolder = value; break;
                case MetadataRecipeOperation.SetHidden: measure.IsHidden = bool.Parse(value); break;
                case MetadataRecipeOperation.SetFormatString: measure.FormatString = value; break;
                case MetadataRecipeOperation.SetDescription: measure.Description = value; break;
                case MetadataRecipeOperation.Rename: measure.Name = value; break;
                default: throw new InvalidOperationException("Recipe operation is not allowlisted.");
            }
        }

        internal void Apply(TabularModelHandler current)
        {
            var target = Plan.Operation == MetadataRecipeOperation.Rename && Plan.Changes.Count == 1 && _objects.TryGetValue(Plan.Changes[0].Id, out var t) ? t : null;
            var context = target != null && !target.IsRemoved ? BuildRenameContext(_handler, target) : null;
            if (!ReferenceEquals(current, _handler) || !_handler.UndoManager.Enabled || _handler.UndoManager.BatchDepth != 0 ||
                !MetadataRecipe.IsCurrent(Plan, Snapshot(), context)) throw new InvalidOperationException("The model changed or is busy. Preview a new recipe.");
            var live = _handler.Model.AllMeasures.ToDictionary(m => m.DaxObjectFullName, StringComparer.Ordinal);
            foreach (var change in Plan.Changes)
                if (!live.TryGetValue(change.Id, out var measure) || !ReferenceEquals(measure, _objects[change.Id]) || measure.IsRemoved)
                    throw new InvalidOperationException("A target was replaced. Preview a new recipe.");
            if (Plan.Changes.Count == 0) return;
            var dependents = Plan.Operation == MetadataRecipeOperation.Rename ? DependentObjects(_handler.Model) : null;
            _handler.BeginUpdate("TE2 Enhanced recipe: " + Plan.Definition.Title);
            var success = false;
            try
            {
                foreach (var change in Plan.Changes)
                {
                    var measure = _objects[change.Id];
                    if (measure.IsRemoved || Read(measure, Plan.Operation) != change.Before) throw new InvalidOperationException("A target changed during apply.");
                    Write(measure, Plan.Operation, change.After);
                    if (Read(measure, Plan.Operation) != change.After) throw new InvalidOperationException("TE2 rejected a proposed property change.");
                }
                if (dependents != null)
                    foreach (var rewrite in Plan.DependentRewrites)
                    {
                        if (!dependents.TryGetValue(rewrite.DependentId, out var obj) || !Enum.TryParse(rewrite.Property, out DAXProperty prop) ||
                            (obj.GetDAX(prop) ?? "") != rewrite.After)
                            throw new InvalidOperationException("TE2 formula fix-up did not produce the previewed expression for " + rewrite.DependentId + ".");
                    }
                success = true;
            }
            finally { _handler.EndUpdate(rollback: !success); }
        }

        /// <summary>
        /// Builds fresh rename facts: reserved names (columns of the target's table) and every DAX property in the model that
        /// either has a TE2-tracked reference to the target or mentions its [Name] textually. Objects are enumerated like TE2's
        /// dependency tree, plus KPIs (which TE2 does not track, so a KPI mention always refuses the rename).
        /// </summary>
        internal static RenameContext BuildRenameContext(TabularModelHandler handler, Measure target)
        {
            var list = new List<RenameDependent>();
            foreach (var pair in DependentObjects(handler.Model))
            {
                var obj = pair.Value;
                if (ReferenceEquals(obj, target)) continue;
                obj.DependsOn.TryGetValue(target, out var refs);
                foreach (var prop in obj.GetDAXProperties())
                {
                    string dax;
                    try { dax = obj.GetDAX(prop) ?? ""; } catch (ArgumentException) { continue; }
                    var spans = (refs ?? new List<ObjectReference>()).Where(r => r.property == prop)
                        .Select(r => new ReferenceSpan(r.from, r.to, r.fullyQualified)).ToList();
                    if (spans.Count == 0 && MetadataRecipe.CountTextualReferences(dax, target.Name) == 0) continue;
                    list.Add(new RenameDependent(pair.Key, prop.ToString(), dax, spans));
                }
            }
            var reserved = target.Table.Columns.Select(c => c.Name);
            return new RenameContext(reserved, list, handler.Settings.AutoFixup);
        }

        /// <summary>Unique display id → DAX-bearing object.</summary>
        internal static Dictionary<string, IDaxDependantObject> DependentObjects(Model model)
        {
            var objects = model.Tables.SelectMany(t => t.GetChildren()).Concat(model.Tables).OfType<IDaxDependantObject>()
                .Concat(model.AllMeasures.Where(m => m.KPI != null).Select(m => (IDaxDependantObject)m.KPI))
                .Concat(model.CalculationGroups.SelectMany(cg => cg.CalculationItems))
                .Concat(model.AllPartitions.OfType<IDaxDependantObject>())
                .Concat(model.Functions)
                .Concat(model.Roles.SelectMany(r => r.TablePermissions))
                .Distinct();
            var result = new Dictionary<string, IDaxDependantObject>(StringComparer.Ordinal);
            foreach (var obj in objects)
            {
                var name = obj is KPI kpi ? "KPI of " + kpi.Measure.DaxObjectFullName
                    : obj is TablePermission tp ? tp.Role.Name + " / " + tp.Table.DaxObjectFullName
                    : obj is CalculationItem ci ? ci.CalculationGroupTable.DaxObjectFullName + " / " + ci.Name
                    : obj is Partition p ? p.Table.DaxObjectFullName + " / " + p.Name
                    : (obj as IDaxObject)?.DaxObjectFullName ?? (obj as ITabularNamedObject)?.Name ?? "?";
                var id = obj.ObjectType + " " + name;
                for (var n = 2; result.ContainsKey(id); n++) id = obj.ObjectType + " " + name + " #" + n;
                result.Add(id, obj);
            }
            return result;
        }
    }
}
