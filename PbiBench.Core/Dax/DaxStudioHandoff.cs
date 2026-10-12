using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PbiBench.Core.Dax
{
    /// <summary>Server and database labels for a DAX Studio handoff. Never a connection string.</summary>
    public sealed class DaxStudioTarget
    {
        public DaxStudioTarget(string server, string database) { Server = server ?? ""; Database = database ?? ""; }
        public string Server { get; }
        public string Database { get; }
        public override string ToString() => Server + " / " + Database;
    }

    public enum DaxStudioHandoffKind
    {
        /// <summary>Start DaxStudio.exe with the server and database labels.</summary>
        LaunchConnected,
        /// <summary>Do not start anything: save the query as .dax and tell the user why and where.</summary>
        SaveAndReport
    }

    /// <summary>The decision for one handoff, with the exact user-facing reason.</summary>
    public sealed class DaxStudioHandoffPlan
    {
        internal DaxStudioHandoffPlan(DaxStudioHandoffKind kind, string? executable, IReadOnlyList<string> arguments, string reason)
        { Kind = kind; Executable = executable; Arguments = arguments; Reason = reason; }
        public DaxStudioHandoffKind Kind { get; }
        public string? Executable { get; }
        public IReadOnlyList<string> Arguments { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// DAX-014: neutral rules for handing a query over to DAX Studio. The only arguments ever produced are
    /// the server label and database label, positionally, which is the argument shape Power BI Desktop's
    /// External Tools registration uses for DAX Studio ("%server%" "%database%"). Anything that looks like a
    /// connection string (key=value pairs, ';', credentials) is refused, so secrets can never reach a command
    /// line. When no executable or no live target is available the plan is SaveAndReport: the host saves the
    /// query as a .dax file and states where it is and why DAX Studio was not started.
    /// </summary>
    public static class DaxStudioHandoff
    {
        public const string ExecutableName = "DaxStudio.exe";
        public const int MaxLabelLength = 512;
        private static readonly string[] SecretMarkers =
            { "password", "pwd", "user id", "uid", "provider", "data source", "initial catalog", "token", "secret", "integrated security" };

        /// <summary>Returns null when the label is acceptable, otherwise the reason it is refused.</summary>
        public static string? CheckLabel(string? value, string name)
        {
            if (string.IsNullOrWhiteSpace(value)) return name + " is not available.";
            var text = value!;
            if (text.Length > MaxLabelLength) return name + " is longer than " + MaxLabelLength + " characters.";
            if (text.Any(c => char.IsControl(c) || c == '"')) return name + " contains control characters or quotes.";
            if (text.IndexOf('=') >= 0 || text.IndexOf(';') >= 0)
                return name + " looks like a connection string; only a server or database label is handed over.";
            var lower = text.ToLowerInvariant();
            if (SecretMarkers.Any(m => lower.StartsWith(m + " ", StringComparison.Ordinal) || lower == m))
                return name + " looks like a connection-string keyword.";
            return null;
        }

        /// <summary>Exactly two arguments: server label, database label. Throws ArgumentException when either is refused.</summary>
        public static IReadOnlyList<string> BuildArguments(DaxStudioTarget target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            var reason = CheckLabel(target.Server, "Server") ?? CheckLabel(target.Database, "Database");
            if (reason != null) throw new ArgumentException(reason);
            return new[] { target.Server.Trim(), target.Database.Trim() };
        }

        /// <summary>A DaxStudio.exe path the host may start: absolute local drive path, exact file name, no UNC.</summary>
        public static bool IsAcceptableExecutable(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var text = path!.Trim();
            if (text.StartsWith("\\\\", StringComparison.Ordinal) || text.StartsWith("//", StringComparison.Ordinal)) return false;
            if (text.Length < 4 || !char.IsLetter(text[0]) || text[1] != ':' || (text[2] != '\\' && text[2] != '/')) return false;
            if (text.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || text.Contains("..")) return false;
            return string.Equals(Path.GetFileName(text), ExecutableName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Candidate DaxStudio.exe locations in priority order: the user-configured path, registry install
        /// locations / App Paths values, then the default per-user and machine install folders. The host
        /// checks existence; unacceptable entries are dropped.
        /// </summary>
        public static IReadOnlyList<string> CandidateExecutables(string? configuredPath, IEnumerable<string>? registryLocations,
            string? localAppData, string? programFiles, string? programFilesX86)
        {
            var candidates = new List<string>();
            void AddFolderOrExe(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                var text = value!.Trim().Trim('"');
                candidates.Add(text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? text : Path.Combine(text, ExecutableName));
            }
            AddFolderOrExe(configuredPath);
            foreach (var location in registryLocations ?? Enumerable.Empty<string>()) AddFolderOrExe(location);
            if (!string.IsNullOrWhiteSpace(localAppData)) candidates.Add(Path.Combine(localAppData!, "Programs", "DAX Studio", ExecutableName));
            if (!string.IsNullOrWhiteSpace(programFiles)) candidates.Add(Path.Combine(programFiles!, "DAX Studio", ExecutableName));
            if (!string.IsNullOrWhiteSpace(programFilesX86)) candidates.Add(Path.Combine(programFilesX86!, "DAX Studio", ExecutableName));
            return candidates.Where(IsAcceptableExecutable).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        /// <summary>Decides launch vs. save-and-report. <paramref name="executable"/> is the resolved existing exe or null.</summary>
        public static DaxStudioHandoffPlan Plan(string? executable, DaxStudioTarget? target)
        {
            if (!IsAcceptableExecutable(executable))
                return new DaxStudioHandoffPlan(DaxStudioHandoffKind.SaveAndReport, null, Array.Empty<string>(),
                    "DAX Studio was not found (configured path, registry, default install folders). Set its location in the Workbench (File > DAX Studio location…) or open the saved .dax file in DAX Studio yourself.");
            if (target == null)
                return new DaxStudioHandoffPlan(DaxStudioHandoffKind.SaveAndReport, executable, Array.Empty<string>(),
                    "No live model connection: DAX Studio needs a server and database. Open the saved .dax file in DAX Studio and connect there.");
            var reason = CheckLabel(target.Server, "Server") ?? CheckLabel(target.Database, "Database");
            if (reason != null)
                return new DaxStudioHandoffPlan(DaxStudioHandoffKind.SaveAndReport, executable, Array.Empty<string>(),
                    reason + " DAX Studio was not started; open the saved .dax file in DAX Studio and connect there.");
            return new DaxStudioHandoffPlan(DaxStudioHandoffKind.LaunchConnected, executable, BuildArguments(target),
                "DAX Studio started for " + target.Server.Trim() + " / " + target.Database.Trim() + " (server and database labels only, no connection string).");
        }
    }
}
