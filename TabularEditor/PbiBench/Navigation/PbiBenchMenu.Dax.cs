using System.Windows.Forms;
using TabularEditor.PbiBench.Dax;

namespace TabularEditor.PbiBench.Navigation
{
    // S2 DAX language service and model usage (DAX-010/011, MODEL-002).
    internal sealed partial class PbiBenchShortcutFilter
    {
        static partial void AddDaxItems(ToolStripMenuItem root)
        {
            root.DropDownItems.Add(new ToolStripSeparator());
            var assist = Item("Expression editor: local DAX assistance (opt-in)", c => c.PbiBench_ToggleExpressionAuthoring());
            assist.ToolTipText = "Ctrl+Space completes, Ctrl+Shift+Space shows parameters in TE2's expression editor. Local only, no network.";
            root.DropDownItems.Add(assist);
            var check = Item("Check current expression (advisory)", c => c.PbiBench_CheckExpression());
            root.DropDownItems.Add(check);
            root.DropDownItems.Add(Item("Model usage and unresolved references…", c => c.PbiBench_ShowModelUsage()));
            root.DropDownOpening += (s, e) => {
                assist.Checked = PbiBenchExpressionEditorAuthoring.IsEnabled;
                check.Enabled = PbiBenchExpressionEditorAuthoring.IsEnabled;
            };
        }
    }
}
