using System;
using System.IO;
using System.Threading;

namespace PbiBench.Core.IO
{
    /// <summary>
    /// <see cref="File.Replace(string, string, string, bool)"/> with an opt-in, bounded retry for OBS-001
    /// (Win32 error 1175 "Unable to remove the file to be replaced", seen on files written moments earlier).
    /// Off by default: the policy stays "fail visibly". Set the environment variable
    /// <c>TE2ENHANCED_REPLACE_RETRY=1</c> before starting the process to allow exactly one retry after 250 ms,
    /// and only for error 1175. Every other error, and a second 1175, surfaces unchanged.
    /// </summary>
    public static class SafeFileReplace
    {
        public const string EnvironmentVariable = "TE2ENHANCED_REPLACE_RETRY";
        internal const int UnableToRemoveReplacedHResult = unchecked((int)0x80070497);
        private static int _retries;

        /// <summary>Read once per process; tests may override it.</summary>
        public static bool RetryEnabled { get; set; } = string.Equals(Environment.GetEnvironmentVariable(EnvironmentVariable), "1", StringComparison.Ordinal);

        /// <summary>How many 1175 failures were absorbed by the retry in this process (evidence for OBS-001).</summary>
        public static int RetriesUsed => Volatile.Read(ref _retries);

        public static void Replace(string source, string destination, string? backup, bool ignoreMetadataErrors = false)
        {
            try { File.Replace(source, destination, backup, ignoreMetadataErrors); }
            catch (IOException ex) when (RetryEnabled && IsUnableToRemoveReplaced(ex))
            {
                Interlocked.Increment(ref _retries);
                Thread.Sleep(250);
                File.Replace(source, destination, backup, ignoreMetadataErrors);
            }
        }

        public static bool IsUnableToRemoveReplaced(Exception ex) =>
            ex is IOException && (ex.HResult == UnableToRemoveReplacedHResult ||
                (ex.Message?.IndexOf("Unable to remove the file to be replaced", StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
    }
}
