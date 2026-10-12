using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using PbiBench.Core.Dax;
using PbiBench.Core.Project;

namespace TabularEditor.PbiBench.Dax
{
    /// <summary>
    /// DAX-014 host side: finds DaxStudio.exe (user-configured path, then registry Uninstall InstallLocation and
    /// App Paths, then default install folders) and starts it directly with the server/database labels from
    /// <see cref="DaxStudioHandoff"/> (UseShellExecute=false, no cmd.exe). The configured path is a per-user
    /// text file %LOCALAPPDATA%\TE2Enhanced\daxstudio-path.txt holding one absolute DaxStudio.exe path.
    /// </summary>
    internal static class PbiBenchDaxStudioLauncher
    {
        internal const string ConfigFileName = "daxstudio-path.txt";

        internal static string DefaultSettingsFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TE2Enhanced");

        internal static string ReadConfiguredPath(string settingsFolder = null)
        {
            try
            {
                var file = Path.Combine(settingsFolder ?? DefaultSettingsFolder, ConfigFileName);
                if (!File.Exists(file) || new FileInfo(file).Length > 4096) return null;
                var value = File.ReadAllText(file).Trim();
                return DaxStudioHandoff.IsAcceptableExecutable(value) ? value : null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { return null; }
        }

        /// <summary>Stores a user-chosen DaxStudio.exe path; refuses anything that is not an existing local DaxStudio.exe.</summary>
        internal static string WriteConfiguredPath(string executable, string settingsFolder = null, Func<string, bool> exists = null)
        {
            exists = exists ?? File.Exists;
            if (!DaxStudioHandoff.IsAcceptableExecutable(executable) || !exists(executable))
                throw new ArgumentException("Choose an existing DaxStudio.exe on a local drive.");
            var folder = settingsFolder ?? DefaultSettingsFolder;
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, ConfigFileName), Path.GetFullPath(executable));
            return Path.GetFullPath(executable);
        }

        internal static string Locate(string settingsFolder = null, Func<string, bool> exists = null, IEnumerable<string> registryLocations = null)
        {
            exists = exists ?? File.Exists;
            var candidates = DaxStudioHandoff.CandidateExecutables(ReadConfiguredPath(settingsFolder), registryLocations ?? RegistryLocations(),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            return candidates.FirstOrDefault(exists);
        }

        internal static ProcessStartInfo BuildStartInfo(DaxStudioHandoffPlan plan)
        {
            if (plan == null || plan.Kind != DaxStudioHandoffKind.LaunchConnected || !DaxStudioHandoff.IsAcceptableExecutable(plan.Executable))
                throw new ArgumentException("Only a resolved DaxStudio.exe with a connected plan is started.");
            if (plan.Arguments.Count != 2) throw new ArgumentException("DAX Studio receives exactly a server label and a database label.");
            return new ProcessStartInfo
            {
                FileName = plan.Executable,
                Arguments = VsCodeHandoff.ToWindowsCommandLine(plan.Arguments),
                UseShellExecute = false,
                CreateNoWindow = false,
                WorkingDirectory = Path.GetDirectoryName(plan.Executable)
            };
        }

        /// <summary>Starts the process once (no retry). Returns null on success, otherwise a user-facing reason.</summary>
        internal static string Start(ProcessStartInfo info)
        {
            try { using (Process.Start(info)) { } return null; }
            catch (Exception) { return "DAX Studio could not be started from " + info.FileName + "."; }
        }

        private static IEnumerable<string> RegistryLocations()
        {
            var result = new List<string>();
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using (var baseKey = RegistryKey.OpenBaseKey(hive, view))
                    {
                        using (var appPath = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\DaxStudio.exe"))
                            if (appPath?.GetValue(null) is string exe) result.Add(exe);
                        using (var uninstall = baseKey.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"))
                        {
                            if (uninstall == null) continue;
                            foreach (var name in uninstall.GetSubKeyNames().Take(2000))
                                using (var key = uninstall.OpenSubKey(name))
                                {
                                    var display = key?.GetValue("DisplayName") as string;
                                    var location = key?.GetValue("InstallLocation") as string;
                                    if (display != null && location != null && display.StartsWith("DAX Studio", StringComparison.OrdinalIgnoreCase))
                                        result.Add(location);
                                }
                        }
                    }
                }
                catch (Exception) { /* registry unavailable: fall back to configured path / default folders */ }
            }
            return result;
        }
    }
}
