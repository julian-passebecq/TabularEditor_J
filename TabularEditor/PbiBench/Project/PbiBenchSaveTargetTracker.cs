using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using PbiBench.Core.Project;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Project
{
    internal enum SaveTargetCheckState { NotApplicable, NoBaseline, Clean, ExternalChanges }

    internal sealed class SaveTargetCheck
    {
        internal SaveTargetCheckState State { get; set; }
        internal string Target { get; set; } = "";
        internal string Message { get; set; } = "";
        internal BaselineStamp Baseline { get; set; }
        internal DiskStamp Disk { get; set; }
        internal ExternalChangeSet Changes { get; set; }
    }

    /// <summary>
    /// Remembers what the loaded model's save target (TMDL/folder or model.bim/.pbit) looked like on
    /// disk when TE2 loaded it, so a later check can list files changed outside TE2 before the user
    /// saves. TE2's own Save is NOT intercepted (deferred: hooking upstream save is invasive and
    /// OBS-001 forbids touching the save path); after a TE2 save the user accepts the new baseline.
    /// </summary>
    internal static class PbiBenchSaveTargetTracker
    {
        private sealed class Entry { internal BaselineStamp Baseline; internal string Target; }
        private static readonly ConditionalWeakTable<TabularModelHandler, Entry> Baselines = new ConditionalWeakTable<TabularModelHandler, Entry>();
        private static bool _subscribed;

        /// <summary>Captures a load-time baseline for each newly loaded model (background, bounded).</summary>
        internal static void Subscribe(TabularEditor.UI.UIController controller)
        {
            if (controller == null || _subscribed) return;
            _subscribed = true;
            controller.ModelLoaded += (s, e) =>
            {
                var handler = controller.Handler;
                if (handler == null) return;
                var type = handler.SourceType; var source = handler.Source;
                Task.Run(() => { try { Remember(handler, type, source, "captured when TE2 loaded the model"); } catch (Exception) { } });
            };
        }

        /// <summary>The disk location TE2 would save to, or null for database/unsaved models.</summary>
        internal static string TargetOf(ModelSourceType type, string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return null;
            switch (type)
            {
                case ModelSourceType.Folder:
                case ModelSourceType.TMDL: return Directory.Exists(source) ? ReviewedFilePlan.NormalizeRoot(source) : null;
                case ModelSourceType.File:
                case ModelSourceType.Pbit: return File.Exists(source) ? Path.GetFullPath(source) : null;
                default: return null;
            }
        }

        internal static FileManifest Capture(string target)
        {
            if (Directory.Exists(target)) return FileManifest.Capture(target);
            var info = new FileInfo(target);
            var entries = info.Exists
                ? new[] { new FileManifestEntry(info.Name, info.Length, info.LastWriteTimeUtc, info.Length <= FileManifest.MaxHashBytes ? ReviewedFilePlan.HashFile(target) : null) }
                : new FileManifestEntry[0];
            return new FileManifest(Path.GetDirectoryName(target), entries, false, DateTime.UtcNow);
        }

        internal static BaselineStamp Remember(TabularModelHandler handler, ModelSourceType type, string source, string reason)
        {
            var target = TargetOf(type, source);
            if (handler == null || target == null) return null;
            var baseline = new BaselineStamp(Capture(target), reason);
            lock (Baselines)
            {
                Baselines.Remove(handler);
                Baselines.Add(handler, new Entry { Baseline = baseline, Target = target });
            }
            return baseline;
        }

        internal static SaveTargetCheck Check(TabularModelHandler handler)
            => handler == null ? new SaveTargetCheck { State = SaveTargetCheckState.NotApplicable, Message = "No model loaded." }
                               : Check(handler, handler.SourceType, handler.Source);

        internal static SaveTargetCheck Check(TabularModelHandler handler, ModelSourceType type, string source)
        {
            var target = TargetOf(type, source);
            if (target == null)
                return new SaveTargetCheck { State = SaveTargetCheckState.NotApplicable,
                    Message = "The loaded model has no disk save target to compare (" + type + "). Disk/Loaded/Live are not equated." };
            Entry entry;
            lock (Baselines) Baselines.TryGetValue(handler, out entry);
            var disk = new DiskStamp(Capture(target));
            if (entry == null || !string.Equals(entry.Target, target, StringComparison.OrdinalIgnoreCase))
                return new SaveTargetCheck { State = SaveTargetCheckState.NoBaseline, Target = target, Disk = disk,
                    Message = "No baseline was captured for this save target (opened before the extension loaded, or Save As changed the target). Accept the current disk state to start tracking." };
            var changes = StateComparison.DiskSinceBaseline(entry.Baseline, disk);
            return new SaveTargetCheck {
                State = changes.HasChanges ? SaveTargetCheckState.ExternalChanges : SaveTargetCheckState.Clean,
                Target = target, Baseline = entry.Baseline, Disk = disk, Changes = changes,
                Message = changes.HasChanges
                    ? changes.Changes.Count + " file change(s) on disk since the baseline (" + entry.Baseline.Reason + "). Saving from TE2 would overwrite or mix with them. Changes made by TE2's own Save also appear here until accepted."
                    : "No disk changes since the baseline (" + entry.Baseline.Reason + ")" + (changes.Incomplete ? "; manifest bounded, not proof." : ".")
            };
        }
    }
}
