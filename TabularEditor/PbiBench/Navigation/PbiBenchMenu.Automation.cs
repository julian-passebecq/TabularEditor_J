using System.Windows.Forms;

namespace TabularEditor.PbiBench.Navigation
{
    // S4 Automation menu items (AUTO-001). The reviewed recipe entry already exists in the shared menu.
    internal sealed partial class PbiBenchShortcutFilter
    {
        static partial void AddAutomationItems(ToolStripMenuItem root)
        {
            var item = Item("C# script workspace (trusted, not sandboxed)…", controller => controller.PbiBench_ShowScriptWorkspace());
            item.Name = "pbiBenchScriptWorkspaceToolStripMenuItem";
            root.DropDownItems.Add(item);
        }
    }
}