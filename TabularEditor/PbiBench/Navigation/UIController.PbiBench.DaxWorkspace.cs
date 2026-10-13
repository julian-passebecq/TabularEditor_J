using System;
using System.IO;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;

namespace TabularEditor.UI
{
    // S3 commands (DAX-013/014). The DAX Workbench itself (tabs, recovery, per-tab handoff) opens via PbiBench_ShowDaxWorkbench.
    public partial class UIController
    {
        /// <summary>
        /// Starts DAX Studio for the loaded model with the server label and database label only. Without a live
        /// connection or without DAX Studio nothing is started and the status bar says why (no query to save here;
        /// the Workbench's File menu saves the query as .dax).
        /// </summary>
        public void PbiBench_DaxStudioHandoff()
        {
            if (Handler?.Model == null) return;
            DaxStudioTarget target = null;
            try
            {
                var server = Handler.Database?.Server;
                if (Handler.IsConnected && server != null && server.Connected && !string.IsNullOrWhiteSpace(server.Name) && !string.IsNullOrWhiteSpace(Handler.Database.Name))
                    target = new DaxStudioTarget(server.Name, Handler.Database.Name);
            }
            catch (Exception) { target = null; }
            var plan = DaxStudioHandoff.Plan(PbiBenchDaxStudioLauncher.Locate(), target);
            if (plan.Kind != DaxStudioHandoffKind.LaunchConnected)
            {
                UI.StatusLabel.Text = "DAX Studio not started: " + plan.Reason.Replace("open the saved .dax file", "open your .dax file");
                return;
            }
            var failure = PbiBenchDaxStudioLauncher.Start(PbiBenchDaxStudioLauncher.BuildStartInfo(plan));
            UI.StatusLabel.Text = failure ?? plan.Reason;
        }

        public void PbiBench_DeleteDaxRecoveryCopies()
        {
            try
            {
                var count = PbiBenchDaxWorkbenchForm.DeleteAllRecoveryCopies(null);
                UI.StatusLabel.Text = "Deleted " + count + " DAX recovery cop" + (count == 1 ? "y" : "ies") + " (script copies untouched).";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                UI.StatusLabel.Text = "DAX recovery copies not deleted: " + ex.Message;
            }
        }
    }
}
