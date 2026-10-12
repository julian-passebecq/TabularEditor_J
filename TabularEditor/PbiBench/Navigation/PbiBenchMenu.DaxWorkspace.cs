using System.Windows.Forms;

namespace TabularEditor.PbiBench.Navigation
{
    // S3 DAX workspace (DAX-013/014): DAX Studio handoff for the loaded model and recovery cleanup.
    // Multi-document tabs, recovery opt-in and per-tab handoff live inside the DAX Workbench itself.
    internal sealed partial class PbiBenchShortcutFilter
    {
        static partial void AddDaxWorkspaceItems(ToolStripMenuItem root)
        {
            root.DropDownItems.Add(new ToolStripSeparator());
            var handoff = Item("Open model in DAX Studio (handoff)", c => c.PbiBench_DaxStudioHandoff());
            handoff.Name = "pbiBenchDaxStudioHandoffToolStripMenuItem";
            handoff.ToolTipText = "Starts DAX Studio with the live server and database labels only (never a connection string).";
            root.DropDownItems.Add(handoff);
            var delete = Item("Delete DAX recovery copies", c => c.PbiBench_DeleteDaxRecoveryCopies());
            delete.Name = "pbiBenchDeleteDaxRecoveryToolStripMenuItem";
            root.DropDownItems.Add(delete);
        }
    }
}
