using System;
using System.Globalization;

namespace PbiBench.Core.Dax
{
    public enum PreviewComparison { Equal, GreaterThan, LessThan }
    public static class DaxTablePreview
    {
        public static string QuoteTable(string name) => "'" + ValidateName(name).Replace("'", "''") + "'";
        public static string QuoteColumn(string name) => "[" + ValidateName(name).Replace("]", "]]") + "]";
        private static string ValidateName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 512 || name.IndexOf('\0') >= 0 || name.IndexOf('\r') >= 0 || name.IndexOf('\n') >= 0)
                throw new ArgumentException("Invalid table or column name.");
            return name;
        }
        public static string Build(string table, int rows, string? column = null, bool filter = false,
            PreviewComparison comparison = PreviewComparison.Equal, object? value = null, bool sort = false, bool descending = false)
        {
            if (rows < 1 || rows > DaxQueryRequest.DefaultMaxRows) throw new ArgumentOutOfRangeException(nameof(rows));
            if (!Enum.IsDefined(typeof(PreviewComparison), comparison)) throw new ArgumentException("Comparison is not supported.");
            var source = QuoteTable(table);
            var reference = filter || sort ? source + QuoteColumn(column!) : "";
            if (filter)
            {
                string predicate;
                if (value == null)
                {
                    if (comparison != PreviewComparison.Equal) throw new ArgumentException("Blank supports equality only.");
                    predicate = "ISBLANK(" + reference + ")";
                }
                else
                {
                    var literal = value is string text ? QuoteString(text) : value is decimal number ? number.ToString(CultureInfo.InvariantCulture) :
                        value is bool boolean ? boolean ? "TRUE()" : "FALSE()" : throw new ArgumentException("Only text, decimal, Boolean or blank filters are supported.");
                    predicate = reference + (comparison == PreviewComparison.Equal ? " == " : comparison == PreviewComparison.GreaterThan ? " > " : " < ") + literal;
                }
                source = "FILTER(" + source + ", " + predicate + ")";
            }
            var order = descending ? "DESC" : "ASC";
            return "EVALUATE\nTOPN(" + rows.ToString(CultureInfo.InvariantCulture) + ", " + source +
                (sort ? ", " + reference + ", " + order : "") + ")" + (sort ? "\nORDER BY " + reference + " " + order : "");
        }
        private static string QuoteString(string text)
        {
            if (text.Length > 4096 || text.IndexOf('\0') >= 0) throw new ArgumentException("Filter text exceeds the supported limit.");
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
