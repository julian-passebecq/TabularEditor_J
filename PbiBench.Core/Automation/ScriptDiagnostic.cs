using System;
using System.Collections.Generic;
using System.Linq;

namespace PbiBench.Core.Automation
{
    public enum ScriptDiagnosticSeverity { Error = 0, Warning = 1, Info = 2 }

    /// <summary>Raw compiler message as the host reads it from CodeDom (kept neutral: no CodeDom type here).</summary>
    public sealed class CompilerMessage
    {
        public CompilerMessage(bool isWarning, string code, string message, int line, int column)
        { IsWarning = isWarning; Code = code ?? ""; Message = message ?? ""; Line = line; Column = column; }
        public bool IsWarning { get; }
        public string Code { get; }
        public string Message { get; }
        public int Line { get; }
        public int Column { get; }
    }

    /// <summary>One row of the script workspace Problems list.</summary>
    public sealed class ScriptDiagnostic
    {
        internal ScriptDiagnostic(string documentId, long generation, ScriptDiagnosticSeverity severity, string code, string message, int line, int column, bool isAdvisory)
        {
            DocumentId = documentId; Generation = generation; Severity = severity; Code = code; Message = message;
            Line = line; Column = column; IsAdvisory = isAdvisory;
        }
        public string DocumentId { get; }
        /// <summary>The <see cref="ScriptDocument.Generation"/> the diagnostic was produced for; older generations are stale.</summary>
        public long Generation { get; }
        public ScriptDiagnosticSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }
        /// <summary>1-based line in the user's script, or 0 when the compiler reported a location in generated code.</summary>
        public int Line { get; }
        public int Column { get; }
        /// <summary>True for the text-scan notes: informational, not a compiler result and not a security check.</summary>
        public bool IsAdvisory { get; }
        public string Location => Line > 0 ? "Ln " + Line + ", Col " + Math.Max(1, Column) : "(generated code)";
    }

    public static class ScriptDiagnostics
    {
        public const int MaxDiagnostics = 200;
        public const int MaxMessageChars = 500;

        /// <summary>
        /// Maps compiler messages onto the user's script. TE2 compiles the script body under <c>#line 1</c>, so in-range
        /// lines map directly; class/struct definitions are hoisted by TE2, and their positions are reported as generated code.
        /// Errors first, then warnings, then by position; bounded to <see cref="MaxDiagnostics"/>.
        /// </summary>
        public static IReadOnlyList<ScriptDiagnostic> FromCompiler(ScriptDocument document, IEnumerable<CompilerMessage> messages)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var lineCount = LineCount(document.Text);
            return (messages ?? Enumerable.Empty<CompilerMessage>())
                .Where(m => m != null)
                .Take(MaxDiagnostics * 4)
                .Select(m => new ScriptDiagnostic(document.Id, document.Generation,
                    m.IsWarning ? ScriptDiagnosticSeverity.Warning : ScriptDiagnosticSeverity.Error,
                    Clean(m.Code, 16), Clean(m.Message, MaxMessageChars),
                    m.Line >= 1 && m.Line <= lineCount ? m.Line : 0,
                    m.Line >= 1 && m.Line <= lineCount ? Math.Max(1, m.Column) : 0, false))
                .OrderBy(d => d.Severity).ThenBy(d => d.Line == 0 ? int.MaxValue : d.Line).ThenBy(d => d.Column)
                .Take(MaxDiagnostics)
                .ToArray();
        }

        private static readonly (string Token, string Note)[] AdvisoryTokens =
        {
            ("#r ", "references an extra assembly (#r) that will be loaded into TE2"),
            ("Process.Start", "can start other programs (Process.Start)"),
            ("File.", "can read or write files (System.IO.File)"),
            ("Directory.", "can read or change folders (System.IO.Directory)"),
            ("WebClient", "can use the network (WebClient)"),
            ("HttpClient", "can use the network (HttpClient)"),
            ("WebRequest", "can use the network (WebRequest)"),
            ("Registry.", "can read or change the Windows registry"),
            ("Environment.", "reads or changes environment settings"),
            ("SaveFile(", "can save model metadata to disk (SaveFile)"),
        };

        /// <summary>
        /// Advisory text scan shown next to the trust prompt. It only lists obvious capability keywords outside line
        /// comments; it is not exhaustive, can be evaded trivially, and is never a reason to call a script safe.
        /// </summary>
        public static IReadOnlyList<ScriptDiagnostic> Advisory(ScriptDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            var result = new List<ScriptDiagnostic>();
            var lines = document.Text.Replace("\r\n", "\n").Split('\n');
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < lines.Length && result.Count < 20; i++)
            {
                var line = lines[i];
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                var code = comment >= 0 ? line.Substring(0, comment) : line;
                foreach (var (token, note) in AdvisoryTokens)
                {
                    var at = code.IndexOf(token, StringComparison.Ordinal);
                    if (at < 0 || !seen.Add(token)) continue;
                    result.Add(new ScriptDiagnostic(document.Id, document.Generation, ScriptDiagnosticSeverity.Info, "ADV",
                        "Advisory (text scan, not a security check): script " + note + ".", i + 1, at + 1, true));
                }
            }
            return result;
        }

        public static string Summary(IEnumerable<ScriptDiagnostic> diagnostics)
        {
            var list = (diagnostics ?? Enumerable.Empty<ScriptDiagnostic>()).Where(d => !d.IsAdvisory).ToArray();
            var errors = list.Count(d => d.Severity == ScriptDiagnosticSeverity.Error);
            var warnings = list.Count(d => d.Severity == ScriptDiagnosticSeverity.Warning);
            return errors + (errors == 1 ? " error, " : " errors, ") + warnings + (warnings == 1 ? " warning" : " warnings");
        }

        private static int LineCount(string text)
        {
            var count = 1;
            foreach (var c in text) if (c == '\n') count++;
            return count;
        }

        private static string Clean(string value, int max) =>
            new string((value ?? "").Select(c => char.IsControl(c) ? ' ' : c).Take(max).ToArray()).Trim();
    }
}
