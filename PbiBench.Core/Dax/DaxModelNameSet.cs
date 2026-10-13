using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PbiBench.Core.Dax
{
    public sealed class DaxTableNames
    {
        public string Name { get; set; } = "";
        public bool IsHidden { get; set; }
        public string[] Columns { get; set; } = Array.Empty<string>();
        public string[] Measures { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Fresh, provider-neutral name set captured from a loaded model by the host binder. It is a
    /// point-in-time value: Revision fingerprints the names so insertions and diagnostics can
    /// reject results computed against an older model shape (rename/delete/add).
    /// </summary>
    public sealed class DaxModelNameSet
    {
        private Dictionary<string, DaxTableNames> _tables = new Dictionary<string, DaxTableNames>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _measures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private HashSet<string> _columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, string> _vocabulary = new Dictionary<string, string>(StringComparer.Ordinal);

        public IReadOnlyList<DaxTableNames> Tables { get; private set; } = Array.Empty<DaxTableNames>();
        public IReadOnlyList<DaxFunctionSignature> Functions { get; private set; } = Array.Empty<DaxFunctionSignature>();
        public string Revision { get; private set; } = "";

        public static DaxModelNameSet Empty { get; } = Create(null, null, null);

        /// <param name="vocabulary">Pinned function vocabulary (any case, dots optional); empty disables unknown-function hints.</param>
        public static DaxModelNameSet Create(IEnumerable<DaxTableNames>? tables, IEnumerable<DaxFunctionSignature>? functions, IEnumerable<string>? vocabulary)
        {
            var set = new DaxModelNameSet();
            var list = (tables ?? Array.Empty<DaxTableNames>()).Where(t => t != null && t.Name.Length > 0)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            set.Tables = list;
            foreach (var t in list)
            {
                set._tables[t.Name] = t;
                foreach (var m in t.Measures) set._measures.Add(m);
                foreach (var c in t.Columns) set._columns.Add(c);
            }
            set.Functions = (functions ?? Array.Empty<DaxFunctionSignature>()).Where(f => f != null)
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var v in vocabulary ?? Array.Empty<string>()) if (!string.IsNullOrWhiteSpace(v)) set._vocabulary[DaxSignatureCatalog.VocabularyKey(v)] = v.Trim().ToUpperInvariant();
            set.Revision = ComputeRevision(list, set.Functions);
            return set;
        }

        public bool HasModel => Tables.Count > 0 || Functions.Count > 0;
        public bool HasVocabulary => _vocabulary.Count > 0;
        public bool IsTable(string name) => _tables.ContainsKey(name ?? "");
        public DaxTableNames? GetTable(string name) => _tables.TryGetValue(name ?? "", out var t) ? t : null;
        public bool IsMeasure(string name) => _measures.Contains(name ?? "");
        public bool IsAnyColumn(string name) => _columns.Contains(name ?? "");
        public bool InVocabulary(string name) => _vocabulary.ContainsKey(DaxSignatureCatalog.VocabularyKey(name));
        /// <summary>Vocabulary spellings as supplied (for example PERCENTILE.INC).</summary>
        public IEnumerable<string> Vocabulary => _vocabulary.Values;

        public DaxFunctionSignature? GetFunction(string name) =>
            Functions.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        public bool TableHasMember(string table, string member)
        {
            var t = GetTable(table);
            return t != null && (t.Columns.Contains(member, StringComparer.OrdinalIgnoreCase) || t.Measures.Contains(member, StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>Deterministic FNV-1a 64-bit fingerprint over ordered names and UDF arities.</summary>
        public static string ComputeRevision(IEnumerable<DaxTableNames> tables, IEnumerable<DaxFunctionSignature> functions)
        {
            var sb = new StringBuilder();
            foreach (var t in tables)
            {
                sb.Append("T\u0001").Append(t.Name).Append('\u0001').Append(t.IsHidden ? '1' : '0').Append('\u0002');
                foreach (var c in t.Columns.OrderBy(x => x, StringComparer.Ordinal)) sb.Append("C\u0001").Append(c).Append('\u0002');
                foreach (var m in t.Measures.OrderBy(x => x, StringComparer.Ordinal)) sb.Append("M\u0001").Append(m).Append('\u0002');
            }
            foreach (var f in functions)
                sb.Append("F\u0001").Append(f.Name).Append('\u0001').Append(f.MinArgs).Append('\u0001').Append(f.MaxArgs).Append('\u0002');
            ulong hash = 14695981039346656037UL;
            foreach (var ch in sb.ToString())
            {
                hash ^= ch;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }
    }
}
