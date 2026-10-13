using System;

namespace PbiBench.Core.Project
{
    /// <summary>
    /// Disk, Loaded, Live, Git and Baseline are distinct identities with distinct types. There is
    /// deliberately no common equality: only manifest-backed stamps (Disk, Baseline) can be compared,
    /// and that comparison is explicit. Everything else is shown side by side, never equated.
    /// </summary>
    public abstract class StateStamp
    {
        protected StateStamp(DateTime capturedUtc) { CapturedUtc = capturedUtc.ToUniversalTime(); }
        public DateTime CapturedUtc { get; }
        public abstract string Kind { get; }
        public abstract string Describe();
        public sealed override bool Equals(object? obj) => ReferenceEquals(this, obj);
        public sealed override int GetHashCode() => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
    }

    /// <summary>What is on disk now (bounded manifest of the project folder).</summary>
    public sealed class DiskStamp : StateStamp
    {
        public DiskStamp(FileManifest manifest) : base(manifest.CapturedUtc) { Manifest = manifest; }
        public FileManifest Manifest { get; }
        public override string Kind => "Disk";
        public override string Describe() => "Disk: " + Manifest.Entries.Count + " files, fingerprint " + Manifest.Fingerprint.Substring(0, 12) +
            (Manifest.Incomplete ? " (bounded, incomplete)" : "") + " at " + CapturedUtc.ToString("u");
    }

    /// <summary>What TE2 has in memory: source descriptor and in-session state, never a disk hash.</summary>
    public sealed class LoadedStamp : StateStamp
    {
        public LoadedStamp(string modelName, string sourceKind, string? sourcePath, bool hasUnsavedChanges, DateTime capturedUtc) : base(capturedUtc)
        { ModelName = modelName ?? ""; SourceKind = sourceKind ?? ""; SourcePath = sourcePath; HasUnsavedChanges = hasUnsavedChanges; }
        public string ModelName { get; }
        public string SourceKind { get; }
        public string? SourcePath { get; }
        public bool HasUnsavedChanges { get; }
        public override string Kind => "Loaded";
        public override string Describe() => "Loaded: " + ModelName + " from " + SourceKind + (SourcePath != null ? " " + SourcePath : "") +
            (HasUnsavedChanges ? " · unsaved TE2 edits" : " · no unsaved TE2 edits");
    }

    /// <summary>A live service/Desktop connection label only (never a connection string).</summary>
    public sealed class LiveStamp : StateStamp
    {
        public LiveStamp(bool connected, string label, DateTime capturedUtc) : base(capturedUtc) { Connected = connected; Label = label ?? ""; }
        public bool Connected { get; }
        public string Label { get; }
        public override string Kind => "Live";
        public override string Describe() => Connected ? "Live: connected (" + Label + "); not compared with disk" : "Live: not connected";
    }

    public sealed class GitStamp : StateStamp
    {
        public GitStamp(bool detected, string? branch, string state, int entryCount, DateTime capturedUtc) : base(capturedUtc)
        { Detected = detected; Branch = branch; State = state ?? GitState.Unknown; EntryCount = entryCount; }
        public bool Detected { get; }
        public string? Branch { get; }
        public string State { get; }
        public int EntryCount { get; }
        public override string Kind => "Git";
        public override string Describe() => Detected ? "Git: " + (Branch ?? "(detached)") + " · " + State + " · " + EntryCount + " status entries" : "Git: not detected";
    }

    /// <summary>A disk manifest the user explicitly accepted as the reference point.</summary>
    public sealed class BaselineStamp : StateStamp
    {
        public BaselineStamp(FileManifest manifest, string reason) : base(manifest.CapturedUtc) { Manifest = manifest; Reason = reason ?? ""; }
        public FileManifest Manifest { get; }
        public string Reason { get; }
        public override string Kind => "Baseline";
        public override string Describe() => "Baseline: " + Reason + " at " + CapturedUtc.ToString("u") + ", " + Manifest.Entries.Count + " files";
    }

    public static class StateComparison
    {
        /// <summary>The only cross-identity comparison offered: disk now versus an accepted baseline.</summary>
        public static ExternalChangeSet DiskSinceBaseline(BaselineStamp baseline, DiskStamp disk)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (disk == null) throw new ArgumentNullException(nameof(disk));
            if (!string.Equals(baseline.Manifest.Root, disk.Manifest.Root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Baseline and disk stamps belong to different project folders.");
            return ExternalChangeDetector.Diff(baseline.Manifest, disk.Manifest);
        }
    }
}
