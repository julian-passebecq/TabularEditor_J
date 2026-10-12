using System;
using System.Globalization;
using System.Text;
using PbiBench.Core.Dax;

namespace PbiBench.Core.Explore
{
    /// <summary>Value family of a profiled column; decides which aggregates are valid DAX for it.</summary>
    public enum ProfileValueKind { Text, Numeric, DateTime, Boolean, Other }

    /// <summary>One draft query. Drafts are text only: nothing here executes, connects or retains rows.</summary>
    public sealed class DaxProfileDraft
    {
        public string Title { get; set; } = "";
        public string Query { get; set; } = "";
        /// <summary>Retained-row cap the Workbench should use for this draft (1..DaxQueryRequest.DefaultMaxRows).</summary>
        public int RowLimit { get; set; }
    }

    /// <summary>
    /// EXP-002 bounded profiling and relationship-coverage drafts. Names are escaped with the same
    /// rules as <see cref="DaxTablePreview"/>; every row-returning result is wrapped in TOPN so the
    /// client never retains more than the requested rows. Aggregates (COUNTROWS, DISTINCTCOUNT, EXCEPT)
    /// still scan whole columns on the server: limits bound returned rows, not server work.
    /// </summary>
    public static class DaxProfileQueryBuilder
    {
        public const int MinTopValues = 1;
        public const int MaxTopValues = 100;
        public const int DefaultTopValues = 10;
        public const int MinSampleRows = 1;
        public const int MaxSampleRows = 1000;
        public const int DefaultSampleRows = 20;

        public const string DraftNotice = "// TE2 Enhanced draft - not executed automatically. Press F5 in the Workbench to run it.";
        public const string ServerWorkNotice = "// Row limits bound returned rows only; aggregates still scan the whole column on the server.";

        public static DaxProfileDraft ColumnProfile(string table, string column, ProfileValueKind kind, int topValues = DefaultTopValues)
        {
            if (topValues < MinTopValues || topValues > MaxTopValues) throw new ArgumentOutOfRangeException(nameof(topValues));
            if (!Enum.IsDefined(typeof(ProfileValueKind), kind)) throw new ArgumentException("Value kind is not supported.", nameof(kind));
            var t = DaxTablePreview.QuoteTable(table);
            var c = t + DaxTablePreview.QuoteColumn(column);
            var n = topValues.ToString(CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append(DraftNotice).Append('\n').Append(ServerWorkNotice).Append('\n');
            sb.Append("// Column profile: row count, distinct, blank").Append(kind == ProfileValueKind.Numeric || kind == ProfileValueKind.DateTime ? ", min, max" : "")
              .Append("; then the ").Append(n).Append(" most frequent values.\n");
            sb.Append("EVALUATE\nROW(\n");
            sb.Append("    \"Rows\", COUNTROWS(").Append(t).Append("),\n");
            sb.Append("    \"Distinct values\", DISTINCTCOUNT(").Append(c).Append("),\n");
            sb.Append("    \"Blank values\", COUNTBLANK(").Append(c).Append(')');
            if (kind == ProfileValueKind.Numeric || kind == ProfileValueKind.DateTime)
            {
                sb.Append(",\n    \"Min\", MIN(").Append(c).Append("),\n");
                sb.Append("    \"Max\", MAX(").Append(c).Append(')');
            }
            sb.Append("\n)\n");
            sb.Append("EVALUATE\nTOPN(").Append(n).Append(", SUMMARIZECOLUMNS(").Append(c).Append(", \"Rows\", COUNTROWS(").Append(t)
              .Append(")), [Rows], DESC)\nORDER BY [Rows] DESC\n");
            return new DaxProfileDraft { Title = "Profile " + t + DaxTablePreview.QuoteColumn(column), Query = sb.ToString(), RowLimit = topValues };
        }

        /// <summary>
        /// Coverage of a relationship from the many side (<paramref name="fromTable"/>) to the one side.
        /// Unmatched keys = distinct many-side values (including real blanks) absent from the one side;
        /// DISTINCT ignores the virtual blank row added for invalid relationships.
        /// </summary>
        public static DaxProfileDraft RelationshipCoverage(string fromTable, string fromColumn, string toTable, string toColumn,
            int sampleRows = DefaultSampleRows)
        {
            if (sampleRows < MinSampleRows || sampleRows > MaxSampleRows) throw new ArgumentOutOfRangeException(nameof(sampleRows));
            var ft = DaxTablePreview.QuoteTable(fromTable);
            var fc = ft + DaxTablePreview.QuoteColumn(fromColumn);
            var tt = DaxTablePreview.QuoteTable(toTable);
            var tc = tt + DaxTablePreview.QuoteColumn(toColumn);
            var n = sampleRows.ToString(CultureInfo.InvariantCulture);
            var unmatched = "EXCEPT(DISTINCT(" + fc + "), DISTINCT(" + tc + "))";
            var sb = new StringBuilder();
            sb.Append(DraftNotice).Append('\n').Append(ServerWorkNotice).Append('\n');
            sb.Append("// Relationship coverage: many-side keys without a match on the one side, unused one-side keys, then up to ")
              .Append(n).Append(" unmatched key samples.\n");
            sb.Append("EVALUATE\nROW(\n");
            sb.Append("    \"Many-side rows\", COUNTROWS(").Append(ft).Append("),\n");
            sb.Append("    \"Many-side distinct keys\", DISTINCTCOUNT(").Append(fc).Append("),\n");
            sb.Append("    \"Many-side blank keys\", COUNTBLANK(").Append(fc).Append("),\n");
            sb.Append("    \"Unmatched keys\", COUNTROWS(").Append(unmatched).Append(") + 0,\n");
            sb.Append("    \"One-side distinct keys\", DISTINCTCOUNT(").Append(tc).Append("),\n");
            sb.Append("    \"Unused one-side keys\", COUNTROWS(EXCEPT(DISTINCT(").Append(tc).Append("), DISTINCT(").Append(fc).Append("))) + 0\n");
            sb.Append(")\n");
            sb.Append("EVALUATE\nTOPN(").Append(n).Append(", ").Append(unmatched).Append(", ").Append(fc).Append(", ASC)\nORDER BY ").Append(fc).Append(" ASC\n");
            return new DaxProfileDraft {
                Title = "Coverage " + fc + " -> " + tc, Query = sb.ToString(), RowLimit = sampleRows
            };
        }
    }
}
