using System.Linq;
using System.Windows.Forms;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Navigation
{
    internal sealed partial class PbiBenchShortcutFilter
    {
        static partial void AddMcpItems(ToolStripMenuItem root)
        {
            root.DropDownItems.Add(new ToolStripSeparator());
            root.DropDownItems.Add(Item("Publish read-only snapshot for MCP…", c =>
            {
                using (var dialog = new TabularEditor.PbiBench.Mcp.PbiBenchSnapshotPublishForm(
                    () => c.Handler?.Model, () => c.Selection?.OfType<ITabularNamedObject>().ToList()))
                    c.PbiBench_ShowDialog(dialog);
            }));
        }
    }
}
