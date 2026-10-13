using System;
using System.Windows.Forms;
using PbiBench.Core.Project;
using TabularEditor.PbiBench.Project;

namespace TabularEditor.UI
{
    public partial class UIController
    {
        /// <summary>Project window with the loaded-model stamp and save-target check wired in.</summary>
        public void PbiBench_ShowProjectWorkspace()
        {
            var handler = Handler;
            using (var dialog = new PbiBenchProjectForm(handler?.Model?.Name ?? "No model", handler?.IsConnected == true))
            {
                dialog.LoadedState = () => ReferenceEquals(handler, Handler) && handler?.Model != null
                    ? new LoadedStamp(handler.Model.Name, handler.SourceType.ToString(), PbiBenchSaveTargetTracker.TargetOf(handler.SourceType, handler.Source),
                        handler.HasUnsavedChanges, DateTime.UtcNow)
                    : null;
                if (handler != null && PbiBenchSaveTargetTracker.TargetOf(handler.SourceType, handler.Source) != null)
                {
                    dialog.SourcePath = PbiBenchSaveTargetTracker.TargetOf(handler.SourceType, handler.Source);
                    dialog.CheckSaveTarget = () => PbiBenchSaveTargetTracker.Check(handler);
                    dialog.AcceptSaveTargetBaseline = () => PbiBenchSaveTargetTracker.Remember(handler, handler.SourceType, handler.Source, "accepted by user");
                }
                PbiBench_ShowDialog(dialog);
            }
        }

        /// <summary>Lists files changed on disk under the loaded model's save target since load (or the last accepted baseline).</summary>
        public void PbiBench_ReviewSaveTarget()
        {
            var handler = Handler;
            if (handler?.Model == null) return;
            SaveTargetCheck check;
            try { check = PbiBenchSaveTargetTracker.Check(handler); }
            catch (Exception) { UI.StatusLabel.Text = "TE2 Enhanced: save target could not be read."; return; }
            var lines = new System.Collections.Generic.List<string> { "Save target: " + (check.Target.Length > 0 ? check.Target : "(none)") };
            if (check.Baseline != null) lines.Add(check.Baseline.Describe());
            if (check.Disk != null) lines.Add(check.Disk.Describe());
            lines.Add(new LoadedStamp(handler.Model.Name, handler.SourceType.ToString(), check.Target, handler.HasUnsavedChanges, DateTime.UtcNow).Describe());
            using (var dialog = new PbiBenchSyncReviewForm("Save target review", lines, check.Changes, check.Message,
                check.State == SaveTargetCheckState.ExternalChanges || check.State == SaveTargetCheckState.NoBaseline))
            {
                if (PbiBench_ShowDialog(dialog) != DialogResult.Yes) return;
                PbiBenchSaveTargetTracker.Remember(handler, handler.SourceType, handler.Source, "accepted by user");
                UI.StatusLabel.Text = "TE2 Enhanced: save target baseline accepted (model not reloaded or saved).";
            }
        }

        public void PbiBench_OpenInVsCode()
        {
            var handler = Handler;
            var target = handler == null ? null : PbiBenchSaveTargetTracker.TargetOf(handler.SourceType, handler.Source);
            if (target == null) { UI.StatusLabel.Text = "TE2 Enhanced: the loaded model has no local folder to open in VS Code."; return; }
            var folder = System.IO.Directory.Exists(target) ? target : System.IO.Path.GetDirectoryName(target);
            UI.StatusLabel.Text = "TE2 Enhanced: " + (PbiBenchVsCodeLauncher.Open(folder) ?? "VS Code started for " + folder + ".");
        }
    }
}
