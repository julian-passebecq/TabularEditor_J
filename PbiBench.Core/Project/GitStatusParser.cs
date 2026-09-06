using System;

namespace PbiBench.Core.Project
{
    /// <summary>
    /// Pure parsing helpers for the bounded Git information PbiBench exposes.
    /// The core library deliberately does not execute Git processes.
    /// </summary>
    public static class GitStatusParser
    {
        public static string ParseState(string? porcelainOutput)
            => string.IsNullOrWhiteSpace(porcelainOutput) ? GitState.Clean : GitState.Modified;

        public static string? ParseBranch(string? branchOutput)
        {
            var value = branchOutput?.Trim();
            if (string.IsNullOrEmpty(value) || string.Equals(value, "HEAD", StringComparison.OrdinalIgnoreCase))
                return null;
            return value;
        }

        public static string? ParseHead(string? headOutput)
        {
            var value = headOutput?.Trim();
            if (string.IsNullOrEmpty(value)) return null;
            if (value.Length < 7 || value.Length > 64) return null;

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return null;
            }

            return value.ToLowerInvariant();
        }
    }
}
