using System.Windows.Forms;

namespace TabularEditor.PbiBench.Navigation
{
    // S7 Explore / Optimize menu items (EXP-002, OPT-001).
    internal sealed partial class PbiBenchShortcutFilter
    {
        static partial void AddExploreItems(ToolStripMenuItem root)
        {
            root.DropDownItems.Add(new ToolStripSeparator());
            var profile = Item("Profile column / relationship coverage (query draft)…", c => c.PbiBench_ShowProfileDraft());
            profile.Name = "pbiBenchProfileDraftToolStripMenuItem";
            profile.ToolTipText = "Builds a bounded DAX draft and opens it unsaved in the Workbench. Nothing runs until you press F5.";
            root.DropDownItems.Add(profile);
            var checks = Item("Model checks: curated BPA + assertions…", c => c.PbiBench_ShowModelChecks());
            checks.Name = "pbiBenchModelChecksToolStripMenuItem";
            checks.ToolTipText = "Read-only: curated rule pack through TE2's BPA engine plus semantic assertions. No rule file is written.";
            root.DropDownItems.Add(checks);
        }
    }
}
