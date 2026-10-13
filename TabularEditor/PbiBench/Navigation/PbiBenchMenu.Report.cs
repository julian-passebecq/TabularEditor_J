using System.Windows.Forms;

namespace TabularEditor.PbiBench.Navigation
{
    // S6 Report: PBIR index, report-to-model lineage, reviewed theme apply (REPORT-001/002).
    internal sealed partial class PbiBenchShortcutFilter
    {
        static partial void AddReportItems(ToolStripMenuItem root)
        {
            root.DropDownItems.Add(new ToolStripSeparator());
            var report = Item("Report (PBIR) index and lineage…", c => c.PbiBench_ShowReport());
            report.ToolTipText = "Read-only PBIR index and report → model lineage; reviewed theme apply for known schema versions. Local files only.";
            root.DropDownItems.Add(report);
        }
    }
}
