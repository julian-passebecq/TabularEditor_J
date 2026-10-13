using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PbiBench.Core.Project
{
    /// <summary>
    /// Builds the argument list for opening a project (and optionally a file at a line) in VS Code.
    /// Only absolute paths are accepted, so no argument can be read as a CLI option; the host passes
    /// the list straight to the executable without a shell.
    /// </summary>
    public static class VsCodeHandoff
    {
        public static IReadOnlyList<string> BuildArguments(string folder, string? file = null, int? line = null, int? column = null, bool newWindow = false)
        {
            var args = new List<string>();
            if (newWindow) args.Add("--new-window");
            args.Add(CheckPath(folder, nameof(folder)));
            if (file != null)
            {
                var full = CheckPath(file, nameof(file));
                if (line.HasValue)
                {
                    if (line.Value < 1 || line.Value > 10000000) throw new ArgumentOutOfRangeException(nameof(line));
                    if (column.HasValue && (column.Value < 1 || column.Value > 100000)) throw new ArgumentOutOfRangeException(nameof(column));
                    args.Add("--goto");
                    args.Add(full + ":" + line.Value + (column.HasValue ? ":" + column.Value : ""));
                }
                else args.Add(full);
            }
            else if (line.HasValue || column.HasValue) throw new ArgumentException("A line needs a file.");
            return args;
        }

        /// <summary>Quotes an argument list into one Windows command line (CommandLineToArgvW rules),
        /// for net48 ProcessStartInfo.Arguments which has no ArgumentList.</summary>
        public static string ToWindowsCommandLine(IEnumerable<string> arguments)
            => string.Join(" ", arguments.Select(QuoteWindowsArgument));

        public static string QuoteWindowsArgument(string argument)
        {
            if (argument == null) throw new ArgumentNullException(nameof(argument));
            if (argument.Any(c => c == '\0' || c == '\r' || c == '\n')) throw new ArgumentException("Arguments may not contain control line breaks.");
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return argument;
            var text = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var c in argument)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { text.Append('\\', backslashes * 2 + 1); text.Append('"'); }
                else { text.Append('\\', backslashes); text.Append(c); }
                backslashes = 0;
            }
            text.Append('\\', backslashes * 2);
            return text.Append('"').ToString();
        }

        /// <summary>Candidate Code.exe locations in priority order (PATH bin folders, then per-user and
        /// machine installs). The host checks existence and adds registry install locations.</summary>
        public static IReadOnlyList<string> CandidateExecutables(string? pathVariable, string? localAppData, string? programFiles, IEnumerable<string>? registryInstallLocations = null)
        {
            var candidates = new List<string>();
            foreach (var location in registryInstallLocations ?? Enumerable.Empty<string>())
                if (IsAbsolute(location)) candidates.Add(Path.Combine(location.Trim().Trim('"'), "Code.exe"));
            foreach (var part in (pathVariable ?? "").Split(';'))
            {
                var dir = part.Trim().Trim('"');
                if (!IsAbsolute(dir)) continue;
                // VS Code puts "<install>\bin" (code.cmd) on PATH; launch the sibling Code.exe, never the .cmd via a shell.
                if (string.Equals(Path.GetFileName(dir.TrimEnd('\\', '/')), "bin", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(Path.Combine(Path.GetDirectoryName(dir.TrimEnd('\\', '/'))!, "Code.exe"));
            }
            if (IsAbsolute(localAppData)) candidates.Add(Path.Combine(localAppData!, "Programs", "Microsoft VS Code", "Code.exe"));
            if (IsAbsolute(programFiles)) candidates.Add(Path.Combine(programFiles!, "Microsoft VS Code", "Code.exe"));
            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static bool IsAbsolute(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var p = path!.Trim().Trim('"');
            return p.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathRooted(p) && !p.StartsWith("\\\\", StringComparison.Ordinal) && p.Length > 2 && p[1] == ':';
        }

        private static string CheckPath(string path, string name)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A path is required.", name);
            if (path.Any(char.IsControl)) throw new ArgumentException("Path contains control characters.", name);
            if (!IsAbsolute(path)) throw new ArgumentException("Only absolute local drive paths are handed to VS Code.", name);
            return Path.GetFullPath(path);
        }
    }
}
