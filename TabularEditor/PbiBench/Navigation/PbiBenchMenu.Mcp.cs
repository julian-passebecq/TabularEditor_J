using System.Linq;
using System.Windows.Forms;
using TabularEditor.TOMWrapper;
using TabularEditor.PbiBench.Mcp;

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
            root.DropDownItems.Add(Item("Galaxy live bridge (explicit instance)…", c =>
            {
                var dialog = new PbiBenchLiveBridgeControlForm(expressions =>
                {
                    var host = new PbiBenchLiveBridgeHost(FormMain.Singleton, () => c.Handler,
                        () => c.Selection.OfType<ITabularNamedObject>().ToList(), expressions, () => c.ExpressionEditorContextGeneration);
                    host.TrackSelection(h => c.Elements.TreeView.SelectionChanged += h, h => c.Elements.TreeView.SelectionChanged -= h);
                    return host;
                });
                dialog.FormClosed += (s, e) => dialog.Dispose();
                dialog.Show(FormMain.Singleton);
            }));
        }
    }
}
