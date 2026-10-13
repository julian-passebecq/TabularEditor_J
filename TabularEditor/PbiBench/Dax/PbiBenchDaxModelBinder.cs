using System;
using System.Collections.Generic;
using System.Linq;
using PbiBench.Core.Dax;
using TabularEditor.TextServices;
using TabularEditor.TOMWrapper;
using TabularEditor.TOMWrapper.Utils;

namespace TabularEditor.PbiBench.Dax
{
    /// <summary>
    /// Projects the loaded TE2 model into a fresh neutral DaxModelNameSet (tables, columns,
    /// measures, UDF signatures) plus TE2's pinned lexer vocabulary. Nothing is cached across
    /// calls except the immutable vocabulary, so callers re-bind before inserting and compare
    /// Revision to reject work computed against an older model shape.
    /// </summary>
    internal static class PbiBenchDaxModelBinder
    {
        private static string[] _vocabulary;

        /// <summary>Function words from the pinned TE2 DAX lexer, with literal spellings such as PERCENTILE.INC.</summary>
        internal static IReadOnlyList<string> PinnedVocabulary
        {
            get
            {
                if (_vocabulary != null) return _vocabulary;
                var words = new List<string>();
                foreach (var field in typeof(DaxToken).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                {
                    if (!field.IsLiteral || field.FieldType != typeof(int)) continue;
                    var value = (int)field.GetRawConstantValue();
                    if (value < DaxToken.ABS || value > DaxToken.KMEANSCLUSTERING) continue;
                    var literal = DAXLexer.DefaultVocabulary.GetLiteralName(value);
                    words.Add(string.IsNullOrEmpty(literal) ? field.Name : literal.Trim('\''));
                }
                _vocabulary = words.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(w => w, StringComparer.OrdinalIgnoreCase).ToArray();
                return _vocabulary;
            }
        }

        internal static DaxModelNameSet Bind(Model model)
        {
            if (model == null) return DaxModelNameSet.Create(null, null, PinnedVocabulary);
            var tables = model.Tables.Where(t => !t.IsRemoved).Select(t => new DaxTableNames {
                Name = t.Name,
                IsHidden = t.IsHidden,
                Columns = t.Columns.Where(c => !c.IsRemoved).Select(c => c.Name).ToArray(),
                Measures = t.Measures.Where(m => !m.IsRemoved).Select(m => m.Name).ToArray()
            }).ToList();
            var functions = new List<DaxFunctionSignature>();
            foreach (var function in Functions(model))
                functions.Add(DaxSignatureCatalog.ParseUserDefined(function.Name, function.Expression) ?? UnknownArity(function.Name));
            return DaxModelNameSet.Create(tables, functions, PinnedVocabulary);
        }

        /// <summary>Resolves a completion item back to the live TE2 object for navigation and stale checks.</summary>
        internal static ITabularNamedObject Resolve(Model model, DaxAuthoringItem item, string tableName)
        {
            if (model == null || item == null) return null;
            switch (item.Kind)
            {
                case "Table":
                    return model.Tables.FirstOrDefault(t => !t.IsRemoved && string.Equals(t.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                case "Measure":
                    return model.AllMeasures.FirstOrDefault(m => !m.IsRemoved && string.Equals(m.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                case "Column":
                    var columns = model.Tables.Where(t => !t.IsRemoved && (tableName.Length == 0 || string.Equals(t.Name, tableName, StringComparison.OrdinalIgnoreCase)))
                        .SelectMany(t => t.Columns);
                    return columns.FirstOrDefault(c => !c.IsRemoved && string.Equals(c.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                case "Function":
                    return Functions(model).FirstOrDefault(f => string.Equals(f.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                default:
                    return null;
            }
        }

        private static IEnumerable<Function> Functions(Model model)
        {
            // UDFs exist only at recent compatibility levels; older models expose an empty collection.
            var functions = model.Functions;
            return functions == null ? Enumerable.Empty<Function>() : functions.Where(f => !f.IsRemoved).ToArray();
        }

        private static DaxFunctionSignature UnknownArity(string name)
        {
            var signature = DaxSignatureCatalog.Create(name, "argument?…", 0, DaxFunctionSignature.Unbounded, "User-defined", "Model user-defined function (parameters not parsed).");
            signature.IsUserDefined = true;
            return signature;
        }
    }
}
