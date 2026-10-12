namespace TabularEditor.UI
{
    public partial class UIController
    {
        /// <summary>
        /// Opens the C# script workspace (AUTO-001). Scripts compile through TE2's ScriptEngine, need per-version trust
        /// confirmation, and run in-process with the user's permissions in one TE2 undo batch (never sandboxed).
        /// </summary>
        public void PbiBench_ShowScriptWorkspace()
        {
            if (Handler?.Model == null) return;
            using (var dialog = new TabularEditor.PbiBench.Automation.PbiBenchScriptWorkspaceForm(() => Handler, () => Selection))
                dialog.ShowDialog(UI.FormMain);
        }
    }
}