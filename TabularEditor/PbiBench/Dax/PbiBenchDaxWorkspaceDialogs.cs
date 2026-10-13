using System.Windows.Forms;

namespace TabularEditor.PbiBench.Dax
{
    /// <summary>Workbench prompts added by the S3 DAX workspace (recovery, external change, DAX Studio). Replaceable in smokes.</summary>
    internal interface IDaxWorkspaceDialogs
    {
        /// <summary>Opt-in disclosure for local recovery copies; true only on an explicit Yes.</summary>
        bool ConfirmRecoveryOptIn(IWin32Window owner, string disclosure);
        /// <summary>Asks whether recovered drafts should be reopened as tabs.</summary>
        bool OfferRestore(IWin32Window owner, int count, string folder);
        /// <summary>Reload from disk would discard unsaved edits; true only on an explicit Yes.</summary>
        bool ConfirmReloadDiscardingEdits(IWin32Window owner, string name);
        /// <summary>Lets the user pick DaxStudio.exe; null when cancelled.</summary>
        string ChooseDaxStudioExecutable(IWin32Window owner);
        /// <summary>Plain report (DAX Studio handoff outcome).</summary>
        void Report(IWin32Window owner, string title, string message);
    }

    internal sealed class PbiBenchDaxWorkspaceDialogs : IDaxWorkspaceDialogs
    {
        public bool ConfirmRecoveryOptIn(IWin32Window owner, string disclosure) => MessageBox.Show(owner,
            disclosure + "\r\n\r\nKeep recovery copies of unsaved DAX documents?", "Local recovery copies",
            MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        public bool OfferRestore(IWin32Window owner, int count, string folder) => MessageBox.Show(owner,
            count + " unsaved DAX document(s) were found in local recovery (" + folder + ").\r\n\r\nReopen them as tabs? " +
            "They open as unsaved drafts; the copies stay until you save or close those tabs.", "DAX recovery",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        public bool ConfirmReloadDiscardingEdits(IWin32Window owner, string name) => MessageBox.Show(owner,
            "Reload " + name + " from disk and discard your unsaved edits in this tab?", "Reload from disk",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

        public string ChooseDaxStudioExecutable(IWin32Window owner)
        {
            using (var dialog = new OpenFileDialog { Filter = "DAX Studio (DaxStudio.exe)|DaxStudio.exe", Title = "Locate DaxStudio.exe", CheckFileExists = true })
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
        }

        public void Report(IWin32Window owner, string title, string message) =>
            MessageBox.Show(owner, message, title, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
