using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PbiBench.Core.Dax
{
    public sealed class DaxParameter
    {
        public string Name { get; set; } = "";
        public bool Optional { get; set; }
        public bool Repeating { get; set; }
        public string Display => Name + (Optional ? "?" : "") + (Repeating ? "…" : "");
    }

    public sealed class DaxFunctionSignature
    {
        public const int Unbounded = int.MaxValue;
        public string Name { get; set; } = "";
        public IReadOnlyList<DaxParameter> Parameters { get; set; } = Array.Empty<DaxParameter>();
        public int MinArgs { get; set; }
        public int MaxArgs { get; set; }
        public string Category { get; set; } = "";
        public string Summary { get; set; } = "";
        public bool IsUserDefined { get; set; }

        public string Display => Format(0);

        /// <summary>Signature text; argumentIndex (1-based) wraps the active parameter in « ».</summary>
        public string Format(int argumentIndex)
        {
            var active = ActiveParameter(argumentIndex);
            var sb = new StringBuilder(Name).Append('(');
            for (var i = 0; i < Parameters.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(i == active ? "«" + Parameters[i].Display + "»" : Parameters[i].Display);
            }
            return sb.Append(')').ToString();
        }

        /// <summary>0-based parameter for a 1-based argument; repeating tails absorb extra arguments.</summary>
        public int ActiveParameter(int argumentIndex)
        {
            if (argumentIndex < 1 || Parameters.Count == 0) return -1;
            if (argumentIndex <= Parameters.Count) return argumentIndex - 1;
            for (var i = Parameters.Count - 1; i >= 0; i--)
                if (Parameters[i].Repeating) return i;
            return -1;
        }

        public bool AcceptsArgumentCount(int count) => count >= MinArgs && count <= MaxArgs;
    }

    /// <summary>
    /// Curated, locally written DAX signature catalog. Names are a subset of TE2's pinned lexer
    /// vocabulary (host smoke checks every entry against it); parameter lists are written fresh
    /// for this project. Absence from the catalog never means a function is invalid.
    /// </summary>
    public sealed class DaxSignatureCatalog
    {
        private const int U = DaxFunctionSignature.Unbounded;
        private readonly Dictionary<string, DaxFunctionSignature> _byName;

        private DaxSignatureCatalog(IEnumerable<DaxFunctionSignature> entries)
        {
            _byName = new Dictionary<string, DaxFunctionSignature>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries) _byName[entry.Name] = entry;
        }

        public static DaxSignatureCatalog Default { get; } = new DaxSignatureCatalog(BuildDefault());

        public IReadOnlyCollection<DaxFunctionSignature> Entries => _byName.Values;

        public bool TryGet(string name, out DaxFunctionSignature signature)
        {
            if (_byName.TryGetValue(name ?? "", out var found)) { signature = found; return true; }
            signature = null!;
            return false;
        }

        /// <summary>Catalog plus model/query user-defined functions (UDFs win on name clash).</summary>
        public DaxSignatureCatalog With(IEnumerable<DaxFunctionSignature> userDefined)
        {
            var all = _byName.Values.ToList();
            all.AddRange(userDefined ?? Array.Empty<DaxFunctionSignature>());
            return new DaxSignatureCatalog(all);
        }

        /// <summary>Normalized vocabulary key: upper case, dots removed (PERCENTILE.INC → PERCENTILEINC).</summary>
        public static string VocabularyKey(string name) => (name ?? "").Replace(".", "").ToUpperInvariant();

        /// <summary>Builds a signature from "a, b?, c…" parameter text with explicit arity.</summary>
        public static DaxFunctionSignature Create(string name, string parameters, int min, int max, string category, string summary)
        {
            var list = new List<DaxParameter>();
            foreach (var raw in (parameters ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = raw.Trim();
                var repeating = p.EndsWith("…", StringComparison.Ordinal);
                if (repeating) p = p.Substring(0, p.Length - 1);
                var optional = p.EndsWith("?", StringComparison.Ordinal);
                if (optional) p = p.Substring(0, p.Length - 1);
                list.Add(new DaxParameter { Name = p, Optional = optional, Repeating = repeating });
            }
            return new DaxFunctionSignature { Name = name, Parameters = list, MinArgs = min, MaxArgs = max, Category = category, Summary = summary };
        }

        /// <summary>
        /// Parses a UDF definition's parameter list: "(a : SCALAR VAL, b) => …". Returns null when
        /// the expression does not start with a parameter list followed by =>.
        /// </summary>
        public static DaxFunctionSignature? ParseUserDefined(string name, string expression)
        {
            var tokens = DaxLexicalScanner.Scan(expression ?? "").Where(t => !t.IsTrivia).ToList();
            if (tokens.Count < 3 || tokens[0].Text != "(") return null;
            var names = new List<string>();
            var depth = 0;
            var expectName = true;
            var i = 0;
            for (; i < tokens.Count; i++)
            {
                var t = tokens[i];
                if (t.Text == "(") { depth++; continue; }
                if (t.Text == ")") { depth--; if (depth == 0) break; continue; }
                if (depth != 1) continue;
                if (t.Text == "," || t.Text == ";") { expectName = true; continue; }
                if (expectName && t.Kind == DaxLexKind.Identifier) { names.Add(t.Text); expectName = false; }
            }
            if (i + 1 >= tokens.Count || tokens[i + 1].Text != "=>") return null;
            var signature = Create(name, string.Join(", ", names), names.Count, names.Count, "User-defined", "Model or query user-defined function.");
            signature.IsUserDefined = true;
            return signature;
        }

        private static IEnumerable<DaxFunctionSignature> BuildDefault()
        {
            const string Agg = "Aggregation", Flt = "Filter", Tbl = "Table", Log = "Logical", Mth = "Math", Txt = "Text", Dte = "Date", Ti = "Time intelligence", Inf = "Information", Rel = "Relationship", Win = "Window";
            Func<string, string, int, int, string, string, DaxFunctionSignature> f = Create;
            return new[] {
                f("SUM", "column", 1, 1, Agg, "Adds all numbers in a column."),
                f("SUMX", "table, expression", 2, 2, Agg, "Sums an expression evaluated for each row of a table."),
                f("AVERAGE", "column", 1, 1, Agg, "Arithmetic mean of a column."),
                f("AVERAGEA", "column", 1, 1, Agg, "Mean of a column, counting non-numeric values."),
                f("AVERAGEX", "table, expression", 2, 2, Agg, "Mean of an expression evaluated per row."),
                f("MIN", "columnOrValue, value?", 1, 2, Agg, "Smallest value in a column, or of two scalars."),
                f("MINA", "column", 1, 1, Agg, "Smallest value, including logical values."),
                f("MINX", "table, expression, variant?", 2, 3, Agg, "Smallest result of an expression per row."),
                f("MAX", "columnOrValue, value?", 1, 2, Agg, "Largest value in a column, or of two scalars."),
                f("MAXA", "column", 1, 1, Agg, "Largest value, including logical values."),
                f("MAXX", "table, expression, variant?", 2, 3, Agg, "Largest result of an expression per row."),
                f("COUNT", "column", 1, 1, Agg, "Counts non-blank numeric/date values."),
                f("COUNTA", "column", 1, 1, Agg, "Counts non-blank values."),
                f("COUNTAX", "table, expression", 2, 2, Agg, "Counts non-blank results of an expression."),
                f("COUNTBLANK", "column", 1, 1, Agg, "Counts blank cells in a column."),
                f("COUNTROWS", "table?", 0, 1, Agg, "Counts rows of a table."),
                f("COUNTX", "table, expression", 2, 2, Agg, "Counts rows where the expression is non-blank."),
                f("DISTINCTCOUNT", "column", 1, 1, Agg, "Counts distinct values, blank included."),
                f("DISTINCTCOUNTNOBLANK", "column", 1, 1, Agg, "Counts distinct values, blank excluded."),
                f("APPROXIMATEDISTINCTCOUNT", "column", 1, 1, Agg, "Estimated distinct count (DirectQuery sources)."),
                f("PRODUCT", "column", 1, 1, Agg, "Multiplies the values of a column."),
                f("PRODUCTX", "table, expression", 2, 2, Agg, "Multiplies an expression evaluated per row."),
                f("MEDIAN", "column", 1, 1, Agg, "Median of a column."),
                f("MEDIANX", "table, expression", 2, 2, Agg, "Median of an expression per row."),
                f("PERCENTILE.INC", "column, k", 2, 2, Agg, "k-th percentile, inclusive."),
                f("PERCENTILE.EXC", "column, k", 2, 2, Agg, "k-th percentile, exclusive."),
                f("PERCENTILEX.INC", "table, expression, k", 3, 3, Agg, "k-th percentile of an expression, inclusive."),
                f("PERCENTILEX.EXC", "table, expression, k", 3, 3, Agg, "k-th percentile of an expression, exclusive."),
                f("STDEV.S", "column", 1, 1, Agg, "Sample standard deviation."),
                f("STDEV.P", "column", 1, 1, Agg, "Population standard deviation."),
                f("STDEVX.S", "table, expression", 2, 2, Agg, "Sample standard deviation of an expression."),
                f("STDEVX.P", "table, expression", 2, 2, Agg, "Population standard deviation of an expression."),
                f("VAR.S", "column", 1, 1, Agg, "Sample variance."),
                f("VAR.P", "column", 1, 1, Agg, "Population variance."),
                f("VARX.S", "table, expression", 2, 2, Agg, "Sample variance of an expression."),
                f("VARX.P", "table, expression", 2, 2, Agg, "Population variance of an expression."),
                f("RANKX", "table, expression, value?, order?, ties?", 2, 5, Agg, "Rank of a value among expression results."),
                f("RANK.EQ", "value, column, order?", 2, 3, Agg, "Rank of a number in a column."),

                f("CALCULATE", "expression, filter?…", 1, U, Flt, "Evaluates an expression in a modified filter context."),
                f("CALCULATETABLE", "table, filter?…", 1, U, Flt, "Evaluates a table in a modified filter context."),
                f("FILTER", "table, condition", 2, 2, Flt, "Rows of a table that satisfy a condition."),
                f("ALL", "tableOrColumn?…", 0, U, Flt, "Removes filters; returns all rows or values."),
                f("ALLEXCEPT", "table, column…", 2, U, Flt, "Removes filters except on the listed columns."),
                f("ALLNOBLANKROW", "tableOrColumn…", 1, U, Flt, "Like ALL without the blank row."),
                f("ALLSELECTED", "tableOrColumn?…", 0, U, Flt, "Removes inner filters, keeping outer selections."),
                f("ALLCROSSFILTERED", "table", 1, 1, Flt, "Removes all filters applied to a table."),
                f("REMOVEFILTERS", "tableOrColumn?…", 0, U, Flt, "CALCULATE modifier that clears filters."),
                f("KEEPFILTERS", "expression", 1, 1, Flt, "Intersects a filter argument with existing filters."),
                f("VALUES", "tableOrColumn", 1, 1, Flt, "Distinct visible values, blank row included."),
                f("DISTINCT", "tableOrColumn", 1, 1, Flt, "Distinct values or rows."),
                f("SELECTEDVALUE", "column, alternateResult?", 1, 2, Flt, "The single visible value, otherwise the alternate."),
                f("HASONEVALUE", "column", 1, 1, Flt, "True when exactly one value is visible."),
                f("HASONEFILTER", "column", 1, 1, Flt, "True when exactly one value is directly filtered."),
                f("ISFILTERED", "tableOrColumn", 1, 1, Flt, "True when directly filtered."),
                f("ISCROSSFILTERED", "tableOrColumn", 1, 1, Flt, "True when filtered directly or through relationships."),
                f("ISINSCOPE", "column", 1, 1, Flt, "True when the column is a grouping level in scope."),
                f("FILTERS", "column", 1, 1, Flt, "Values directly filtered on a column."),
                f("EARLIER", "column, number?", 1, 2, Flt, "Value from an outer row context."),
                f("EARLIEST", "column", 1, 1, Flt, "Value from the outermost row context."),
                f("LOOKUPVALUE", "resultColumn, searchColumn, searchValue, searchPair?…, alternateResult?", 3, U, Flt, "Value of a column for rows matching search criteria."),
                f("TREATAS", "table, column…", 2, U, Flt, "Applies a table's values as filters on model columns."),
                f("SELECTEDMEASURE", "", 0, 0, Flt, "Measure in context (calculation items)."),
                f("SELECTEDMEASURENAME", "", 0, 0, Flt, "Name of the measure in context."),
                f("SELECTEDMEASUREFORMATSTRING", "", 0, 0, Flt, "Format string of the measure in context."),
                f("ISSELECTEDMEASURE", "measure…", 1, U, Flt, "True when the measure in context is listed."),

                f("RELATED", "column", 1, 1, Rel, "Value from the one side of a relationship."),
                f("RELATEDTABLE", "table", 1, 1, Rel, "Related rows from the many side."),
                f("USERELATIONSHIP", "column1, column2", 2, 2, Rel, "Activates an inactive relationship in CALCULATE."),
                f("CROSSFILTER", "column1, column2, direction", 3, 3, Rel, "Sets cross-filter direction in CALCULATE."),

                f("ADDCOLUMNS", "table, name, expression, nameExpressionPair?…", 3, U, Tbl, "Adds calculated columns to a table."),
                f("SELECTCOLUMNS", "table, nameOrColumn, expression?…", 2, U, Tbl, "Projects named expressions over a table."),
                f("SUMMARIZE", "table, groupBy?…, name?, expression?…", 1, U, Tbl, "Groups a table by columns."),
                f("SUMMARIZECOLUMNS", "groupByFilterOrNameExpression…", 1, U, Tbl, "Grouped summary with filters and named expressions."),
                f("GROUPBY", "table, groupBy?…, name?, expression?…", 1, U, Tbl, "Groups a table using CURRENTGROUP iterations."),
                f("ROW", "name, expression, nameExpressionPair?…", 2, U, Tbl, "Single-row table of named expressions."),
                f("DATATABLE", "name, type, nameTypePair?…, data", 3, U, Tbl, "Inline table with typed columns."),
                f("TOPN", "count, table, orderBy?, order?, orderPair?…", 2, U, Tbl, "Top rows by order expressions."),
                f("GENERATE", "table1, table2", 2, 2, Tbl, "Cross product of each row with an evaluated table."),
                f("GENERATEALL", "table1, table2", 2, 2, Tbl, "GENERATE keeping rows with empty results."),
                f("GENERATESERIES", "start, end, increment?", 2, 3, Tbl, "Single-column table of a numeric series."),
                f("CROSSJOIN", "table, table…", 2, U, Tbl, "Cartesian product of tables."),
                f("UNION", "table, table…", 2, U, Tbl, "Concatenates tables with matching columns."),
                f("INTERSECT", "table1, table2", 2, 2, Tbl, "Rows present in both tables."),
                f("EXCEPT", "table1, table2", 2, 2, Tbl, "Rows of the first table not in the second."),
                f("NATURALINNERJOIN", "leftTable, rightTable", 2, 2, Tbl, "Inner join on common columns."),
                f("NATURALLEFTOUTERJOIN", "leftTable, rightTable", 2, 2, Tbl, "Left outer join on common columns."),
                f("CALENDAR", "startDate, endDate", 2, 2, Tbl, "Contiguous date table between two dates."),
                f("CALENDARAUTO", "fiscalYearEndMonth?", 0, 1, Tbl, "Date table covering model dates."),
                f("CONTAINS", "table, column, value, columnValuePair?…", 3, U, Tbl, "True when a row with the values exists."),
                f("CONTAINSROW", "table, value…", 2, U, Tbl, "True when a row with the values exists (IN operator)."),
                f("ISEMPTY", "table", 1, 1, Tbl, "True when a table has no rows."),
                f("SAMPLE", "count, table, orderBy, order?, orderPair?…", 3, U, Tbl, "Evenly distributed sample of rows."),

                f("OFFSET", "delta, relation?, orderBy?, blanks?, partitionBy?, matchBy?, reset?", 1, 7, Win, "Row at a relative position."),
                f("INDEX", "position, relation?, orderBy?, blanks?, partitionBy?, matchBy?, reset?", 1, 7, Win, "Row at an absolute position."),
                f("WINDOW", "from, fromType?, to, toType?, relation?, orderBy?, blanks?, partitionBy?, matchBy?, reset?", 2, 10, Win, "Rows in a sliding window."),
                f("ORDERBY", "expression, order?, expressionOrderPair?…", 1, U, Win, "Sort specification for window functions."),
                f("PARTITIONBY", "column…", 1, U, Win, "Partition specification for window functions."),
                f("MATCHBY", "column…", 1, U, Win, "Row identity specification for window functions."),

                f("IF", "condition, valueIfTrue, valueIfFalse?", 2, 3, Log, "Conditional result."),
                f("IF.EAGER", "condition, valueIfTrue, valueIfFalse?", 2, 3, Log, "IF that evaluates both branches eagerly."),
                f("SWITCH", "expression, value, result, valueResultPair?…, else?", 3, U, Log, "Matches an expression against values."),
                f("AND", "logical1, logical2", 2, 2, Log, "True when both arguments are true."),
                f("OR", "logical1, logical2", 2, 2, Log, "True when either argument is true."),
                f("NOT", "logical", 1, 1, Log, "Logical negation."),
                f("TRUE", "", 0, 0, Log, "Logical true."),
                f("FALSE", "", 0, 0, Log, "Logical false."),
                f("IFERROR", "value, valueIfError", 2, 2, Log, "Alternate value on error."),
                f("COALESCE", "value, value…", 2, U, Log, "First non-blank argument."),

                f("BLANK", "", 0, 0, Inf, "Blank value."),
                f("ISBLANK", "value", 1, 1, Inf, "True when the value is blank."),
                f("ISERROR", "value", 1, 1, Inf, "True when the value is an error."),
                f("ISNUMBER", "value", 1, 1, Inf, "True when the value is numeric."),
                f("ISTEXT", "value", 1, 1, Inf, "True when the value is text."),
                f("ISNONTEXT", "value", 1, 1, Inf, "True when the value is not text."),
                f("ISLOGICAL", "value", 1, 1, Inf, "True when the value is logical."),
                f("ISONORAFTER", "value, value, order?, valueValueOrder?…", 2, U, Inf, "Start-at style comparison over value pairs."),
                f("ERROR", "text", 1, 1, Inf, "Raises an error with a message."),
                f("USERNAME", "", 0, 0, Inf, "Domain user name of the connection."),
                f("USERPRINCIPALNAME", "", 0, 0, Inf, "User principal name of the connection."),
                f("NAMEOF", "value", 1, 1, Inf, "Fully qualified name of a column or measure."),
                f("EVALUATEANDLOG", "value, label?, maxRows?", 1, 3, Inf, "Returns the value and logs it for diagnostics."),

                f("DIVIDE", "numerator, denominator, alternateResult?", 2, 3, Mth, "Safe division."),
                f("ABS", "number", 1, 1, Mth, "Absolute value."),
                f("ROUND", "number, digits", 2, 2, Mth, "Rounds to a number of digits."),
                f("ROUNDUP", "number, digits", 2, 2, Mth, "Rounds away from zero."),
                f("ROUNDDOWN", "number, digits", 2, 2, Mth, "Rounds toward zero."),
                f("MROUND", "number, multiple", 2, 2, Mth, "Rounds to a multiple."),
                f("INT", "number", 1, 1, Mth, "Rounds down to an integer."),
                f("TRUNC", "number, digits?", 1, 2, Mth, "Truncates to an integer or digits."),
                f("MOD", "number, divisor", 2, 2, Mth, "Remainder of a division."),
                f("QUOTIENT", "numerator, denominator", 2, 2, Mth, "Integer part of a division."),
                f("POWER", "number, power", 2, 2, Mth, "Number raised to a power."),
                f("SQRT", "number", 1, 1, Mth, "Square root."),
                f("EXP", "number", 1, 1, Mth, "e raised to a power."),
                f("LN", "number", 1, 1, Mth, "Natural logarithm."),
                f("LOG", "number, base?", 1, 2, Mth, "Logarithm to a base (default 10)."),
                f("LOG10", "number", 1, 1, Mth, "Base-10 logarithm."),
                f("SIGN", "number", 1, 1, Mth, "Sign of a number."),
                f("CEILING", "number, significance", 2, 2, Mth, "Rounds up to a multiple."),
                f("FLOOR", "number, significance", 2, 2, Mth, "Rounds down to a multiple."),
                f("RAND", "", 0, 0, Mth, "Random number in [0, 1)."),
                f("RANDBETWEEN", "bottom, top", 2, 2, Mth, "Random integer between two bounds."),
                f("PI", "", 0, 0, Mth, "The constant pi."),
                f("CONVERT", "expression, dataType", 2, 2, Mth, "Converts to a data type."),
                f("CURRENCY", "value", 1, 1, Mth, "Converts to the currency type."),
                f("VALUE", "text", 1, 1, Mth, "Converts text to a number."),
                f("FIXED", "number, decimals?, noCommas?", 1, 3, Txt, "Formats a rounded number as text."),

                f("CONCATENATE", "text1, text2", 2, 2, Txt, "Joins two text values."),
                f("CONCATENATEX", "table, expression, delimiter?, orderBy?, order?, orderPair?…", 2, U, Txt, "Joins expression results over rows."),
                f("COMBINEVALUES", "delimiter, expression, expression…", 3, U, Txt, "Joins values with a delimiter."),
                f("FORMAT", "value, formatString, localeName?", 2, 3, Txt, "Formats a value as text."),
                f("LEFT", "text, numChars?", 1, 2, Txt, "Leading characters."),
                f("RIGHT", "text, numChars?", 1, 2, Txt, "Trailing characters."),
                f("MID", "text, start, numChars", 3, 3, Txt, "Characters from a start position."),
                f("LEN", "text", 1, 1, Txt, "Number of characters."),
                f("UPPER", "text", 1, 1, Txt, "Upper case."),
                f("LOWER", "text", 1, 1, Txt, "Lower case."),
                f("TRIM", "text", 1, 1, Txt, "Removes extra spaces."),
                f("SUBSTITUTE", "text, oldText, newText, instance?", 3, 4, Txt, "Replaces matching text."),
                f("REPLACE", "oldText, start, numChars, newText", 4, 4, Txt, "Replaces characters by position."),
                f("SEARCH", "findText, withinText, startNum?, notFoundValue?", 2, 4, Txt, "Case-insensitive position of text."),
                f("FIND", "findText, withinText, startNum?, notFoundValue?", 2, 4, Txt, "Case-sensitive position of text."),
                f("REPT", "text, count", 2, 2, Txt, "Repeats text."),
                f("EXACT", "text1, text2", 2, 2, Txt, "Case-sensitive equality."),
                f("UNICHAR", "number", 1, 1, Txt, "Character for a code point."),
                f("UNICODE", "text", 1, 1, Txt, "Code point of the first character."),
                f("CONTAINSSTRING", "withinText, findText", 2, 2, Txt, "Case-insensitive containment."),
                f("CONTAINSSTRINGEXACT", "withinText, findText", 2, 2, Txt, "Case-sensitive containment."),
                f("PATH", "idColumn, parentColumn", 2, 2, Txt, "Delimited ancestor path for a parent-child hierarchy."),
                f("PATHITEM", "path, position, type?", 2, 3, Txt, "Item at a position in a path."),
                f("PATHLENGTH", "path", 1, 1, Txt, "Number of items in a path."),
                f("PATHCONTAINS", "path, item", 2, 2, Txt, "True when a path contains an item."),

                f("DATE", "year, month, day", 3, 3, Dte, "Date from parts."),
                f("TIME", "hour, minute, second", 3, 3, Dte, "Time from parts."),
                f("YEAR", "date", 1, 1, Dte, "Year of a date."),
                f("QUARTER", "date", 1, 1, Dte, "Quarter of a date."),
                f("MONTH", "date", 1, 1, Dte, "Month of a date."),
                f("DAY", "date", 1, 1, Dte, "Day of month."),
                f("HOUR", "datetime", 1, 1, Dte, "Hour of a time."),
                f("MINUTE", "datetime", 1, 1, Dte, "Minute of a time."),
                f("SECOND", "datetime", 1, 1, Dte, "Second of a time."),
                f("WEEKDAY", "date, returnType?", 1, 2, Dte, "Day of week number."),
                f("WEEKNUM", "date, returnType?", 1, 2, Dte, "Week number."),
                f("TODAY", "", 0, 0, Dte, "Current date."),
                f("NOW", "", 0, 0, Dte, "Current date and time."),
                f("UTCTODAY", "", 0, 0, Dte, "Current UTC date."),
                f("UTCNOW", "", 0, 0, Dte, "Current UTC date and time."),
                f("EOMONTH", "startDate, months", 2, 2, Dte, "Last day of a month offset."),
                f("EDATE", "startDate, months", 2, 2, Dte, "Date offset by months."),
                f("DATEDIFF", "date1, date2, interval", 3, 3, Dte, "Interval boundaries crossed between dates."),
                f("DATEVALUE", "text", 1, 1, Dte, "Converts text to a date."),
                f("TIMEVALUE", "text", 1, 1, Dte, "Converts text to a time."),
                f("YEARFRAC", "startDate, endDate, basis?", 2, 3, Dte, "Fraction of a year between dates."),

                f("DATEADD", "dates, numberOfIntervals, interval", 3, 3, Ti, "Dates shifted by an interval."),
                f("DATESYTD", "dates, yearEndDate?", 1, 2, Ti, "Year-to-date dates."),
                f("DATESQTD", "dates", 1, 1, Ti, "Quarter-to-date dates."),
                f("DATESMTD", "dates", 1, 1, Ti, "Month-to-date dates."),
                f("DATESBETWEEN", "dates, startDate, endDate", 3, 3, Ti, "Dates within a range."),
                f("DATESINPERIOD", "dates, startDate, numberOfIntervals, interval", 4, 4, Ti, "Dates in a period from a start date."),
                f("SAMEPERIODLASTYEAR", "dates", 1, 1, Ti, "Dates shifted back one year."),
                f("PARALLELPERIOD", "dates, numberOfIntervals, interval", 3, 3, Ti, "Full parallel period shifted by intervals."),
                f("PREVIOUSDAY", "dates", 1, 1, Ti, "Previous day."),
                f("PREVIOUSMONTH", "dates", 1, 1, Ti, "Previous month's dates."),
                f("PREVIOUSQUARTER", "dates", 1, 1, Ti, "Previous quarter's dates."),
                f("PREVIOUSYEAR", "dates, yearEndDate?", 1, 2, Ti, "Previous year's dates."),
                f("NEXTDAY", "dates", 1, 1, Ti, "Next day."),
                f("NEXTMONTH", "dates", 1, 1, Ti, "Next month's dates."),
                f("NEXTQUARTER", "dates", 1, 1, Ti, "Next quarter's dates."),
                f("NEXTYEAR", "dates, yearEndDate?", 1, 2, Ti, "Next year's dates."),
                f("TOTALYTD", "expression, dates, filter?, yearEndDate?", 2, 4, Ti, "Year-to-date value."),
                f("TOTALQTD", "expression, dates, filter?", 2, 3, Ti, "Quarter-to-date value."),
                f("TOTALMTD", "expression, dates, filter?", 2, 3, Ti, "Month-to-date value."),
                f("FIRSTDATE", "dates", 1, 1, Ti, "First visible date."),
                f("LASTDATE", "dates", 1, 1, Ti, "Last visible date."),
                f("STARTOFMONTH", "dates", 1, 1, Ti, "First date of the month."),
                f("ENDOFMONTH", "dates", 1, 1, Ti, "Last date of the month."),
                f("STARTOFQUARTER", "dates", 1, 1, Ti, "First date of the quarter."),
                f("ENDOFQUARTER", "dates", 1, 1, Ti, "Last date of the quarter."),
                f("STARTOFYEAR", "dates, yearEndDate?", 1, 2, Ti, "First date of the year."),
                f("ENDOFYEAR", "dates, yearEndDate?", 1, 2, Ti, "Last date of the year."),
                f("FIRSTNONBLANK", "column, expression", 2, 2, Ti, "First value where the expression is not blank."),
                f("LASTNONBLANK", "column, expression", 2, 2, Ti, "Last value where the expression is not blank."),
                f("FIRSTNONBLANKVALUE", "column, expression", 2, 2, Ti, "First non-blank expression result."),
                f("LASTNONBLANKVALUE", "column, expression", 2, 2, Ti, "Last non-blank expression result."),
                f("OPENINGBALANCEMONTH", "expression, dates, filter?", 2, 3, Ti, "Value at the end of the previous month."),
                f("CLOSINGBALANCEMONTH", "expression, dates, filter?", 2, 3, Ti, "Value at the end of the month."),
            };
        }
    }
}
