using System;
using System.Collections.Generic;

namespace PbiBench.Core.Project
{
    public sealed class GitPathState
    {
        public string Status { get; set; } = "";
        public string Path { get; set; } = "";
        public string OriginalPath { get; set; } = "";
    }

    public sealed class GitWorkingTree
    {
        public string Branch { get; private set; } = "";
        public IReadOnlyList<GitPathState> Entries { get; private set; } = Array.Empty<GitPathState>();
        public bool IsTruncated { get; private set; }

        public static GitWorkingTree ParsePorcelain(string output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (output.Length > 1024 * 1024) throw new ArgumentException("Git status exceeds the input limit.");
            var parts = output.Split('\0');
            var result = new GitWorkingTree();
            var entries = new List<GitPathState>();
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.Length == 0) continue;
                if (part.StartsWith("## ", StringComparison.Ordinal)) { result.Branch = part.Substring(3); continue; }
                if (part.Length < 4 || part[2] != ' ') throw new ArgumentException("Invalid Git porcelain status.");
                if (entries.Count == 4096) { result.IsTruncated = true; break; }
                var entry = new GitPathState { Status = part.Substring(0, 2), Path = part.Substring(3) };
                if (entry.Status.IndexOf('R') >= 0 || entry.Status.IndexOf('C') >= 0)
                {
                    if (++i >= parts.Length || parts[i].Length == 0) throw new ArgumentException("Incomplete Git rename record.");
                    entry.OriginalPath = parts[i];
                }
                entries.Add(entry);
            }
            result.Entries = entries;
            return result;
        }
    }
}
