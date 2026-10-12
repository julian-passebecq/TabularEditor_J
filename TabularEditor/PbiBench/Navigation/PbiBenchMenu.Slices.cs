using System.Windows.Forms;

namespace TabularEditor.PbiBench.Navigation
{
    // Menu extension points. Each area implements its partial in PbiBenchMenu.<Area>.cs so
    // parallel work never edits the shared menu builder; unimplemented partials compile away.
    internal sealed partial class PbiBenchShortcutFilter
    {
        static partial void AddDaxItems(ToolStripMenuItem root);
        static partial void AddDaxWorkspaceItems(ToolStripMenuItem root);
        static partial void AddAutomationItems(ToolStripMenuItem root);
        static partial void AddProjectItems(ToolStripMenuItem root);
        static partial void AddReportItems(ToolStripMenuItem root);
        static partial void AddExploreItems(ToolStripMenuItem root);

        /// <summary>Runs a command only while a model is loaded (shared by area partials).</summary>
        internal static ToolStripMenuItem Item(string text, System.Action<TabularEditor.UI.UIController> command)
        {
            var item = new ToolStripMenuItem(text);
            item.Click += (s, e) => RunCommand(command);
            return item;
        }
    }
}
