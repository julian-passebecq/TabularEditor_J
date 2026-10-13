using System;
using System.IO;
using System.Linq;
using System.Text;

namespace PbiBench.Core.Dax
{
    public static class DaxCsvExport
    {
        public static void Write(TextWriter writer, DaxQueryResult result)
        {
            if (result.State != DaxQueryExecutionState.Completed) throw new InvalidOperationException("Only completed results can be exported.");
            writer.WriteLine(string.Join(",", result.Columns.Select(Quote)));
            foreach (var row in result.Rows)
                writer.WriteLine(string.Join(",", row.Select(v => Quote(DaxResultRetention.FormatValue(v)))));
        }
        public static void Save(string path, DaxQueryResult result, Action<TextWriter, DaxQueryResult>? write = null)
        {
            var fullPath = Path.GetFullPath(path);
            var temporary = Path.Combine(Path.GetDirectoryName(fullPath)!, ".pbibench-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(true, true)))
                {
                    writer.NewLine = "\r\n";
                    (write ?? Write)(writer, result);
                }
                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, null);
                else File.Move(temporary, fullPath);
            }
            finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch { } }
        }
        private static string Quote(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    }
}
