using System;
using System.Windows.Forms;
using TabularEditor.PbiBench.Project;

namespace TabularEditor.PbiBench.Navigation
{
    internal sealed partial class PbiBenchShortcutFilter
    {
        static partial void AddProjectItems(ToolStripMenuItem root)
        {
            // Replace the historical read-only Project entry (added by the shared builder just before
            // this partial) with the S5 Project window, keeping its position in the menu.
            var index = root.DropDownItems.Count;
            for (var i = 0; i < root.DropDownItems.Count; i++)
                if (root.DropDownItems[i].Text == "Project (read-only)…") { index = i; root.DropDownItems.RemoveAt(i); break; }
            root.DropDownItems.Insert(Math.Min(index, root.DropDownItems.Count), Item("Project…", c => c.PbiBench_ShowProjectWorkspace()));
            root.DropDownItems.Insert(Math.Min(index + 1, root.DropDownItems.Count), Item("Review save target for external changes…", c => c.PbiBench_ReviewSaveTarget()));
            root.DropDownItems.Insert(Math.Min(index + 2, root.DropDownItems.Count), Item("Open model folder in VS Code", c => c.PbiBench_OpenInVsCode()));
            // Load-time baselines for the save-target review (models opened before this point get none).
            PbiBenchSaveTargetTracker.Subscribe(FormMain.Singleton?.UI);
        }
    }
}
