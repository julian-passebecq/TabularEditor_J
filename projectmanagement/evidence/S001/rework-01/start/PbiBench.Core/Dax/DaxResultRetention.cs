using System;
using System.Globalization;

namespace PbiBench.Core.Dax
{
    /// <summary>Retained estimate v1: 64 bytes/result, 32/array or row, 8/reference,
    /// 24/string + 2/UTF-16 code unit, 24/boxed scalar, 32/binary + bytes.
    /// Includes schema names and row references. Not a bound on provider buffering or server work.</summary>
    public static class DaxResultRetention
    {
        public const int MaxColumns = 256;
        public const int MaxCellBytes = 1024 * 1024;
        public static bool TryEstimate(object? value, out long bytes)
        {
            if (value == null || value == DBNull.Value) { bytes = 0; return true; }
            if (value is string text) { bytes = 24L + 2L * text.Length; return true; }
            if (value is byte[] binary) { bytes = 32L + binary.Length; return true; }
            if (value is bool || value is char || value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong || value is float || value is double ||
                value is decimal || value is DateTime || value is DateTimeOffset || value is TimeSpan || value is Guid)
            { bytes = 24; return true; }
            bytes = 0; return false;
        }
        public static string FormatValue(object? value)
        {
            if (value == null || value == DBNull.Value) return string.Empty;
            if (!TryEstimate(value, out _)) return "[Unsupported value]";
            if (value is DateTime date) return date.ToString("O", CultureInfo.InvariantCulture);
            if (value is DateTimeOffset offset) return offset.ToString("O", CultureInfo.InvariantCulture);
            if (value is TimeSpan span) return span.ToString("c", CultureInfo.InvariantCulture);
            if (value is byte[] bytes) return Convert.ToBase64String(bytes);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }
}
