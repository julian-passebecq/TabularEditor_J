using System.Windows.Forms;

namespace TabularEditor
{
    public partial class FormMain
    {
        /// <summary>
        /// Minimal shell hook for PbiBench. Keeping the shortcut in a separate partial file
        /// avoids edits to the large upstream FormMain implementation/designer.
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.P)
                && UI?.Handler?.Model != null)
            {
                UI.PbiBench_ShowQuickOpen();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
