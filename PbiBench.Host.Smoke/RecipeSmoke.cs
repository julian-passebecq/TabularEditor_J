using System;
using PbiBench.Core.Automation;
using TabularEditor.PbiBench.Automation;
using TabularEditor.TOMWrapper;

internal static class RecipeSmoke
{
    internal static void Run(TabularModelHandler handler)
    {
        var table = handler.Model.AddTable("Recipe synthetic");
        var first = table.AddMeasure("First", "1");
        var second = table.AddMeasure("Second", "2");
        var recipe = new PbiBenchMetadataRecipe(handler, table.Measures, MetadataRecipeOperation.SetDisplayFolder, "Reviewed");
        Check(first.DisplayFolder == "" && recipe.Plan.Changes.Count == 2, "Preview mutated model or missing diff.");
        recipe.Apply(handler);
        Check(first.DisplayFolder == "Reviewed" && second.DisplayFolder == "Reviewed", "Apply incomplete.");
        handler.UndoManager.Undo();
        Check(first.DisplayFolder == "" && second.DisplayFolder == "", "Single-batch undo failed.");
        var stale = new PbiBenchMetadataRecipe(handler, table.Measures, MetadataRecipeOperation.SetHidden, "True");
        second.DisplayFolder = "External edit";
        Reject(() => stale.Apply(handler));
        Check(!first.IsHidden && !second.IsHidden, "Stale plan partially applied.");
        var rollback = new PbiBenchMetadataRecipe(handler, table.Measures, MetadataRecipeOperation.SetDisplayFolder, "Rollback test");
        System.ComponentModel.PropertyChangingEventHandler fail = (s, e) => { if (e.PropertyName == "DisplayFolder") throw new InvalidOperationException("Synthetic setter rejection"); };
        second.PropertyChanging += fail;
        try { Reject(() => rollback.Apply(handler)); }
        finally { second.PropertyChanging -= fail; }
        Check(first.DisplayFolder == "" && second.DisplayFolder == "External edit" && handler.UndoManager.BatchDepth == 0, "Partial apply rollback failed.");
        Reject(() => MetadataRecipe.Propose(new[] { new MeasureMetadata("x", "", false) }, new[] { "x" }, (MetadataRecipeOperation)99, "arbitrary code"));
        Reject(() => MetadataRecipe.Propose(new[] { new MeasureMetadata("x", "", false) }, new[] { "x" }, MetadataRecipeOperation.SetHidden, "not bool"));
        table.Delete();
        Console.WriteLine("Restricted recipes: exact preview, apply, single undo, stale rejection and allowlist PASS");
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } catch (InvalidOperationException) { return; } throw new Exception("Expected recipe rejection."); }
}
