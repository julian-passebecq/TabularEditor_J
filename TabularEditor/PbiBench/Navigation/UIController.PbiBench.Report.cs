using System.Windows.Forms;
using TabularEditor.PbiBench.Project;
using TabularEditor.PbiBench.Report;

namespace TabularEditor.UI
{
    public partial class UIController
    {
        /// <summary>PBIR report index + lineage against the loaded model; OK navigates to the chosen model object.</summary>
        public void PbiBench_ShowReport()
        {
            var handler = Handler;
            var reports = PbiBenchReportBinder.FindReports(handler);
            using (var dialog = new PbiBenchReportForm(() => ReferenceEquals(handler, Handler) ? handler?.Model : null,
                reports.Count > 0 ? reports[0] : null, PbiBenchProjectPaths.JournalRoot))
            {
                if (dialog.ShowDialog(UI.FormMain) != DialogResult.OK) return;
                var selected = dialog.SelectedObject;
                if (selected == null || selected.IsRemoved) { UI.StatusLabel.Text = "TE2 Enhanced: selected object is no longer available."; return; }
                Goto(selected);
                UI.StatusLabel.Text = "Report lineage: " + selected.Name;
            }
        }
    }
}
