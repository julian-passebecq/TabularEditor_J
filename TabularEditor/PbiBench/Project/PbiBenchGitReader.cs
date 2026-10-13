using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PbiBench.Core.Project;

namespace TabularEditor.PbiBench.Project
{
    internal static class PbiBenchGitReader
    {
        internal static async Task<GitWorkingTree> ReadAsync(string directory, CancellationToken token)
        {
            var executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')
                .Select(p => p.Trim().Trim('"')).Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathRooted(p))
                .Select(p => Path.Combine(p, "git.exe")).FirstOrDefault(File.Exists);
            if (executable == null) throw new InvalidOperationException("Git is unavailable.");
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            using (var process = new Process { StartInfo = new ProcessStartInfo {
                FileName = executable, WorkingDirectory = Path.GetFullPath(directory),
                Arguments = "--no-optional-locks -c core.fsmonitor=false status --porcelain=v1 --branch -z --untracked-files=normal",
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            } })
            {
                token.ThrowIfCancellationRequested();
                foreach (var variable in new[] { "GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_CONFIG_COUNT", "GIT_CONFIG_PARAMETERS" })
                    process.StartInfo.EnvironmentVariables.Remove(variable);
                process.Start();
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using (timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(); } catch (Exception) { } }))
                {
                    try {
                        var output = ReadBounded(process.StandardOutput, timeout.Token);
                        var errors = ReadBounded(process.StandardError, timeout.Token);
                        await Task.WhenAll(output, errors).ConfigureAwait(false);
                        timeout.Token.ThrowIfCancellationRequested();
                        if (!process.WaitForExit(1000) || process.ExitCode != 0) throw new InvalidOperationException("Git status could not be read.");
                        return GitWorkingTree.ParsePorcelain(output.Result);
                    }
                    finally { try { if (!process.HasExited) process.Kill(); } catch (Exception) { } }
                }
            }
        }
        private static async Task<string> ReadBounded(StreamReader reader, CancellationToken token)
        {
            var text = new StringBuilder();
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
            {
                token.ThrowIfCancellationRequested();
                if (text.Length + count > 1024 * 1024) throw new InvalidOperationException("Git output exceeds the display limit.");
                text.Append(buffer, 0, count);
            }
            return text.ToString();
        }
    }
}
