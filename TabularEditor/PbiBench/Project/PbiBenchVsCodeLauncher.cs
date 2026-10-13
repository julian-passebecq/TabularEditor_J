using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using PbiBench.Core.Project;

namespace TabularEditor.PbiBench.Project
{
    /// <summary>
    /// Opens a folder (or file at a line) in VS Code. Code.exe is located from registry install
    /// locations, PATH "bin" folders and the default per-user/machine install folders; it is started
    /// directly with an argument list (no cmd.exe, no shell execute, no code.cmd).
    /// </summary>
    internal static class PbiBenchVsCodeLauncher
    {
        internal static string Locate(Func<string, bool> exists = null)
        {
            exists = exists ?? File.Exists;
            var candidates = VsCodeHandoff.CandidateExecutables(Environment.GetEnvironmentVariable("PATH"),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), RegistryInstallLocations());
            return candidates.FirstOrDefault(exists);
        }

        internal static ProcessStartInfo BuildStartInfo(string executable, IReadOnlyList<string> arguments)
        {
            if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) ||
                !string.Equals(Path.GetFileName(executable), "Code.exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Only a resolved Code.exe is launched.");
            return new ProcessStartInfo {
                FileName = executable, Arguments = VsCodeHandoff.ToWindowsCommandLine(arguments),
                UseShellExecute = false, CreateNoWindow = false, WorkingDirectory = Path.GetDirectoryName(executable)
            };
        }

        /// <summary>Returns null on success, or a user-facing reason (nothing is retried).</summary>
        internal static string Open(string folder, string file = null, int? line = null)
        {
            IReadOnlyList<string> arguments;
            try { arguments = VsCodeHandoff.BuildArguments(folder, file, line); }
            catch (ArgumentException) { return "VS Code handoff needs an absolute local folder path."; }
            var executable = Locate();
            if (executable == null) return "VS Code was not found (registry, PATH or default install folders). Open the folder manually: " + folder;
            try { using (Process.Start(BuildStartInfo(executable, arguments))) { } return null; }
            catch (Exception) { return "VS Code could not be started from " + executable + "."; }
        }

        private static IEnumerable<string> RegistryInstallLocations()
        {
            var result = new List<string>();
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default))
                    using (var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                    {
                        if (uninstall == null) continue;
                        foreach (var name in uninstall.GetSubKeyNames().Take(2000))
                            using (var key = uninstall.OpenSubKey(name))
                            {
                                var display = key?.GetValue("DisplayName") as string;
                                var location = key?.GetValue("InstallLocation") as string;
                                if (display != null && location != null && display.StartsWith("Microsoft Visual Studio Code", StringComparison.OrdinalIgnoreCase) &&
                                    display.IndexOf("Insiders", StringComparison.OrdinalIgnoreCase) < 0)
                                    result.Add(location);
                            }
                    }
                }
                catch (Exception) { /* registry unavailable: fall back to PATH/default folders */ }
            }
            return result;
        }
    }
}
