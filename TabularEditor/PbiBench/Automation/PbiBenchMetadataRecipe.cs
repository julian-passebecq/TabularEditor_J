using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Automation;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Automation
{
    internal sealed class PbiBenchMetadataRecipe
    {
        private readonly TabularModelHandler _handler;
        private readonly Dictionary<string, Measure> _objects;
        internal MetadataRecipePlan Plan { get; }
        internal PbiBenchMetadataRecipe(TabularModelHandler handler, IEnumerable<Measure> targets, MetadataRecipeOperation operation, string value)
        {
            _handler = handler;
            _objects = handler.Model.AllMeasures.Take(MetadataRecipe.MaxMeasures + 1).ToDictionary(m => m.DaxObjectFullName, StringComparer.Ordinal);
            Plan = MetadataRecipe.Propose(Snapshot(), targets.Select(m => m.DaxObjectFullName), operation, value);
        }
        private IEnumerable<MeasureMetadata> Snapshot() => _handler.Model.AllMeasures.Select(m => new MeasureMetadata(m.DaxObjectFullName, m.DisplayFolder, m.IsHidden));
        internal void Apply(TabularModelHandler current)
        {
            if (!ReferenceEquals(current, _handler) || !_handler.UndoManager.Enabled || _handler.UndoManager.BatchDepth != 0 ||
                !MetadataRecipe.IsCurrent(Plan, Snapshot())) throw new InvalidOperationException("The model changed or is busy. Preview a new recipe.");
            var live = _handler.Model.AllMeasures.ToDictionary(m => m.DaxObjectFullName, StringComparer.Ordinal);
            foreach (var change in Plan.Changes)
                if (!live.TryGetValue(change.Id, out var measure) || !ReferenceEquals(measure, _objects[change.Id]) || measure.IsRemoved)
                    throw new InvalidOperationException("A target was replaced. Preview a new recipe.");
            if (Plan.Changes.Count == 0) return;
            _handler.BeginUpdate("TE2 Enhanced metadata recipe");
            var success = false;
            try {
                foreach (var change in Plan.Changes)
                {
                    var measure = _objects[change.Id];
                    var before = Plan.Operation == MetadataRecipeOperation.SetDisplayFolder ? measure.DisplayFolder : measure.IsHidden.ToString();
                    if (measure.IsRemoved || before != change.Before) throw new InvalidOperationException("A target changed during apply.");
                    if (Plan.Operation == MetadataRecipeOperation.SetDisplayFolder) measure.DisplayFolder = change.After;
                    else if (Plan.Operation == MetadataRecipeOperation.SetHidden) measure.IsHidden = bool.Parse(change.After);
                    else throw new InvalidOperationException("Recipe operation is not allowlisted.");
                    var actual = Plan.Operation == MetadataRecipeOperation.SetDisplayFolder ? measure.DisplayFolder : measure.IsHidden.ToString();
                    if (actual != change.After) throw new InvalidOperationException("TE2 rejected a proposed property change.");
                }
                success = true;
            }
            finally { _handler.EndUpdate(rollback: !success); }
        }
    }
}
