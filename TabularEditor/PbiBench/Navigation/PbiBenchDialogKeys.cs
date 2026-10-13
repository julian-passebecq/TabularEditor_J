using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TabularEditor.PbiBench.Navigation
{
    /// <summary>
    /// Shared keyboard conventions for PbiBench dialogs (Light QA D14/D17): Esc closes a dialog unless an
    /// open drop-down should consume it, and after a dialog closes focus goes back to the control that had
    /// it (or to the model tree when that was the menu strip or nothing).
    /// </summary>
    internal static class PbiBenchDialogKeys
    {
        /// <summary>Call from ProcessCmdKey: closes the form on Esc and returns true when handled.</summary>
        public static bool CloseOnEscape(Form form, Keys keyData)
        {
            if (keyData != Keys.Escape || form == null) return false;
            if (form.ActiveControl is ComboBox combo && combo.DroppedDown) return false;
            if (form.Modal) form.DialogResult = DialogResult.Cancel;
            form.Close();
            return true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        /// <summary>The managed control that currently has keyboard focus, or null.</summary>
        public static Control FocusedControl()
        {
            try { var handle = GetFocus(); return handle == IntPtr.Zero ? null : Control.FromChildHandle(handle); }
            catch (Exception) { return null; }
        }

        /// <summary>Returns focus after a dialog: the previous control when it can take focus, else the fallback (model tree).</summary>
        public static void RestoreFocus(Control previous, Control fallback)
        {
            if (IsUsable(previous) && !(previous is ToolStrip) && previous.Focus()) return;
            if (IsUsable(fallback)) fallback.Focus();
        }

        private static bool IsUsable(Control control) =>
            control != null && !control.IsDisposed && control.IsHandleCreated && control.CanFocus;
    }
}
