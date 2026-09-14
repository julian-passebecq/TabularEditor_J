using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Dax;
using PbiBench.Core.Navigation;
using TabularEditor.PbiBench.Navigation;
using TabularEditor.TOMWrapper;
using TabularEditor.TOMWrapper.Utils;

namespace TabularEditor.PbiBench.Dax
{
    internal static class PbiBenchDaxAuthoring
    {
        internal static DaxAuthoringToken[] Tokens(string text)
        {
            if (text.Length > DaxAuthoring.MaxTextLength) throw new ArgumentException("Local assistance is limited to 256 Ki characters.");
            return DaxDependencyHelper.Tokenize(text, true).Select(t => new DaxAuthoringToken {
                Text = t.Text, Start = t.StartIndex, Length = t.StopIndex - t.StartIndex + 1,
                IsTriviaOrLiteral = t.CommentOrWhitespace || t.Type == DaxToken.STRING_LITERAL ||
                    t.Type == DaxToken.TABLE || t.Type == DaxToken.COLUMN_OR_MEASURE
            }).ToArray();
        }

        internal static List<DaxAuthoringItem> Items(PbiBenchQuickOpenIndex index)
        {
            var items = new List<DaxAuthoringItem>();
            // Reuse the pinned upstream lexer vocabulary, not an independently updated catalog.
            foreach (var field in typeof(DaxToken).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (!field.IsLiteral || field.FieldType != typeof(int)) continue;
                var value = (int)field.GetRawConstantValue();
                if (value < DaxToken.ABS || value > DaxToken.KMEANSCLUSTERING) continue;
                items.Add(new DaxAuthoringItem { Name = field.Name, InsertText = field.Name + "(", Kind = "Function",
                    Detail = "Pinned TE2 vocabulary; endpoint support may vary." });
            }
            if (index != null)
                foreach (var entry in index.Descriptors)
                {
                    if (entry.Kind != SemanticObjectKind.Table && entry.Kind != SemanticObjectKind.Column &&
                        entry.Kind != SemanticObjectKind.Measure && entry.Kind != SemanticObjectKind.Function &&
                        entry.Kind != SemanticObjectKind.CalculationGroup) continue;
                    var obj = index.Resolve(entry.Id) as IDaxObject;
                    if (obj == null) continue;
                    items.Add(new DaxAuthoringItem { Name = entry.Name, InsertText = obj.DaxObjectFullName,
                        Kind = entry.Kind.ToString(), ObjectId = entry.Id,
                        Detail = entry.DisplayPath + (entry.IsHidden ? " · hidden" : "") });
                }
            return items;
        }
    }
}
