using System;
using System.Windows.Forms;
using TabularEditor.UI;

namespace TabularEditor.PbiBench.Navigation
{
    /// <summary>
    /// Application-lifetime keyboard hook for PbiBench commands. This avoids adding another
    /// ProcessCmdKey override to FormMain and therefore keeps the upstream form implementation
    /// untouched. The filter is installed on the TE2 UI thread when UIController is initialized.
    /// </summary>
    internal sealed class PbiBenchShortcutFilter : IMessageFilter
    {
        private const int WmKeyDown = 0x0100;
        private static PbiBenchShortcutFilter _instance;

        private PbiBenchShortcutFilter()
        {
        }

        public static PbiBenchShortcutFilter Install()
        {
            if (_instance != null) return _instance;

            _instance = new PbiBenchShortcutFilter();
            Application.AddMessageFilter(_instance);
            return _instance;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WmKeyDown) return false;
            if ((Keys)(int)m.WParam != Keys.P) return false;

            var modifiers = Control.ModifierKeys;
            if ((modifiers & Keys.Control) != Keys.Control) return false;
            if ((modifiers & (Keys.Alt | Keys.Shift)) != Keys.None) return false;

            var form = FormMain.Singleton;
            if (form == null || form.IsDisposed || !ReferenceEquals(Form.ActiveForm, form))
                return false;

            var controller = form.UI;
            if (controller?.Handler?.Model == null) return false;

            controller.PbiBench_ShowQuickOpen();
            return true;
        }
    }
}
