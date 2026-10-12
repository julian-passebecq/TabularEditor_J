using System;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;
using TabularEditor.TOMWrapper;

/// <summary>S2 host checks: pinned vocabulary, TE2 quoting/lexer parity, expression-editor and workbench insert/undo/stale.</summary>
internal static class ExpressionAuthoringSmoke
{
    private static void Check(bool condition, string message) => SliceSmokes.DaxCheck(condition, message);

    internal static void Run(TabularModelHandler handler)
    {
        var model = handler.Model;

        // Every curated signature names a function in TE2's pinned lexer vocabulary.
        var vocabulary = PbiBenchDaxModelBinder.PinnedVocabulary;
        var keys = new System.Collections.Generic.HashSet<string>(vocabulary.Select(DaxSignatureCatalog.VocabularyKey));
        var missing = DaxSignatureCatalog.Default.Entries.Where(e => !keys.Contains(DaxSignatureCatalog.VocabularyKey(e.Name))).Select(e => e.Name).ToArray();
        Check(missing.Length == 0, "Catalog functions outside the pinned TE2 vocabulary: " + string.Join(", ", missing));
        Check(vocabulary.Contains("PERCENTILE.INC") && vocabulary.Contains("SUMX"), "Pinned vocabulary literal spellings.");

        // Core scanner agrees with TE2's lexer on delimiter structure.
        foreach (var text in new[] {
            "EVALUATE ROW(\"(ignored)\", 1)", "VAR x = {1, 2} RETURN SUMX(x, [Value])",
            "/* ( outer /* nested } */ ) */ EVALUATE 'O''Brien'", "EVALUATE ROW(\"v\", 'Odd table'[Closing]]name]) -- (",
            "EVALUATE ROW(\"v\", 1", "EVALUATE {1, 2)", "EVALUATE ROW(\"v\", 1))", "SUMX(T, ( 1 )) // )" })
        {
            var te2 = DaxAuthoring.CheckStructure(PbiBenchDaxAuthoring.Tokens(text)).Count;
            var core = DaxDiagnostics.Analyze(text).Count(d => d.Code == DaxDiagnosticCodes.Delimiter);
            Check((te2 == 0) == (core == 0), "Scanner/TE2 lexer delimiter disagreement: " + text);
        }

        var table = model.AddTable("S2 O'Brien");
        var column = table.AddCalculatedColumn("Amount]x", "1");
        var measure = table.AddMeasure("S2 Total", "1");
        try
        {
            // Core quoting equals TE2's DaxObjectFullName for awkward names.
            Check(DaxLexicalScanner.QuoteTable(table.Name) == table.DaxObjectFullName, "Table quoting differs from TE2: " + table.DaxObjectFullName);
            Check(DaxLexicalScanner.QuoteTable(table.Name) + DaxLexicalScanner.QuoteColumn(column.Name) == column.DaxObjectFullName, "Column quoting differs from TE2: " + column.DaxObjectFullName);
            Check(DaxLexicalScanner.QuoteColumn(measure.Name) == measure.DaxObjectName, "Measure quoting differs from TE2: " + measure.DaxObjectName);

            var surface = PbiBenchFctbSurface.CreateDetached();
            var authoring = new PbiBenchExpressionEditorAuthoring(surface, () => model);
            const string partial = "SUMX('S2 O''Brien'[Am";
            surface.Reset(partial, partial.Length);
            var session = authoring.BeginCompletion();
            var item = session.Items.Single(x => x.Kind == "Column");
            Check(item.InsertText == "[Amount]]x]" && session.Context.TableName == "S2 O'Brien", "Table-scoped completion with escapes.");
            Check(authoring.TryApply(session, item, out var reason) && surface.Text == "SUMX('S2 O''Brien'[Amount]]x]", "Insert failed: " + reason + " → " + surface.Text);
            surface.Undo();
            Check(surface.Text == partial, "Editor undo must restore the text in one step: " + surface.Text);

            session = authoring.BeginCompletion();
            surface.Reset(partial + " ", partial.Length + 1);
            Check(!authoring.TryApply(session, session.Items.First(), out reason) && reason.Contains("changed"), "Stale text must be rejected.");

            surface.Reset("[S2 T", 5);
            session = authoring.BeginCompletion();
            var measureItem = session.Items.Single(x => x.InsertText == "[S2 Total]");
            Check(PbiBenchDaxModelBinder.Resolve(model, measureItem, "") == measure, "Completion item resolves to the live measure.");
            measure.Name = "S2 Renamed";
            Check(!authoring.TryApply(session, measureItem, out reason) && reason.Contains("renamed") && surface.Text == "[S2 T", "Stale after rename must be rejected.");
            Check(!authoring.BeginCompletion().Items.Any(x => x.InsertText == "[S2 Total]"), "Renamed measure must not be offered.");
            handler.UndoManager.Undo();
            Check(measure.Name == "S2 Total", "TOM undo of rename.");
            session = authoring.BeginCompletion();
            Check(session.Items.Any(x => x.InsertText == "[S2 Total]"), "Undone rename must be offered again.");
            measure.Delete();
            Check(!authoring.TryApply(session, measureItem, out reason) && surface.Text == "[S2 T", "Stale after delete must be rejected.");
            Check(!authoring.BeginCompletion().Items.Any(x => x.InsertText == "[S2 Total]"), "Deleted measure must not be offered.");

            surface.Reset("DIVIDE(1, ", 10);
            Check(authoring.SignatureHelp().StartsWith("DIVIDE(numerator, «denominator»", StringComparison.Ordinal), "Expression signature help: " + authoring.SignatureHelp());
            surface.Reset("[S2 Total] + SUMX('S2 O''Brien', 1)", 0);
            var issues = authoring.Check();
            Check(issues.Any(d => d.Code == DaxDiagnosticCodes.UnknownColumnOrMeasure && d.Reference == "[S2 Total]"), "Deleted measure reference must be an advisory issue.");
            Check(issues.All(d => d.Message.EndsWith("(advisory)", StringComparison.Ordinal)), "Advisory label.");

            WorkbenchCompletion(model, column);
        }
        finally
        {
            if (!table.IsRemoved) table.Delete();
        }
        Console.WriteLine("Expression authoring: vocabulary, TE2 quoting/lexer parity, insert/undo, stale rename/delete, workbench PASS");
    }

    private static void WorkbenchCompletion(Model model, Column column)
    {
        using (var form = new PbiBenchDaxWorkbenchForm(null, "", "Offline smoke check"))
        {
            var handle = form.Handle;
            form.AuthoringModel = () => model;
            var editor = (RichTextBox)typeof(PbiBenchDaxWorkbenchForm).GetField("_queryEditor", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(form);
            editor.Text = "EVALUATE ROW(\"v\", SUM('S2 O''Brien'[Amo]))";
            editor.Select(editor.Text.IndexOf("Amo", StringComparison.Ordinal) + 3, 0);
            var session = form.BeginWorkbenchCompletion();
            var item = session.Items.Single(x => x.Kind == "Column");
            Check(form.TryApplyWorkbenchCompletion(session, item, out var reason), "Workbench insert: " + reason);
            Check(editor.Text == "EVALUATE ROW(\"v\", SUM('S2 O''Brien'[Amount]]x]))", "Workbench replaces the whole bracket token: " + editor.Text);
            session = form.BeginWorkbenchCompletion();
            editor.AppendText(" ");
            Check(!form.TryApplyWorkbenchCompletion(session, item, out reason), "Workbench stale text must be rejected.");
            session = form.BeginWorkbenchCompletion();
            column.Name = "Renamed]x";
            Check(!form.TryApplyWorkbenchCompletion(session, item, out reason) && reason.Contains("renamed"), "Workbench stale rename must be rejected.");
        }
    }
}
