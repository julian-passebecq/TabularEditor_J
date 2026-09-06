using System;
using System.Windows.Forms;
using TabularEditor.UI;

namespace TabularEditor.PbiBench.Navigation
{
    /// <summary>
    /// Application-lifetime command host for the isolated PbiBench layer. It owns keyboard
    /// routing and installs a small Tools > PbiBench menu at runtime, avoiding edits to the
    /// upstream FormMain implementation and WinForms designer.
    /// </summary>
    internal sealed class PbiBenchShortcutFilter : IMessageFilter
    {
        private const int WmKeyDown = 0x0100;
        private const string MenuName = "pbiBenchToolStripMenuItem";
        private static PbiBenchShortcutFilter _instance;

        private PbiBenchShortcutFilter()
        {
            Application.Idle += EnsureMenuOnIdle;
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
            if ((modifiers & Keys.Alt) == Keys.Alt) return false;

            var form = FormMain.Singleton;
            if (form == null || form.IsDisposed || !ReferenceEquals(Form.ActiveForm, form))
                return false;

            var controller = form.UI;
            if (controller?.Handler?.Model == null) return false;

            if ((modifiers & Keys.Shift) == Keys.Shift)
            {
                controller.PbiBench_ShowSemanticView();
                return true;
            }

            controller.PbiBench_ShowQuickOpen();
            return true;
        }

        private void EnsureMenuOnIdle(object sender, EventArgs e)
        {
            var form = FormMain.Singleton;
            if (form == null || form.IsDisposed || form.MainMenuStrip == null) return;

            ToolStripMenuItem toolsMenu = null;
            foreach (ToolStripItem item in form.MainMenuStrip.Items)
            {
                var candidate = item as ToolStripMenuItem;
                if (candidate != null && string.Equals(candidate.Name, "toolsToolStripMenuItem", StringComparison.Ordinal))
                {
                    toolsMenu = candidate;
                    break;
                }
            }

            if (toolsMenu == null) return;

            foreach (ToolStripItem item in toolsMenu.DropDownItems)
            {
                if (string.Equals(item.Name, MenuName, StringComparison.Ordinal))
                {
                    Application.Idle -= EnsureMenuOnIdle;
                    return;
                }
            }

            var pbiBench = new ToolStripMenuItem
            {
                Name = MenuName,
                Text = "PbiBench"
            };

            var quickOpen = new ToolStripMenuItem
            {
                Name = "pbiBenchQuickOpenToolStripMenuItem",
                Text = "Quick Open",
                ShortcutKeyDisplayString = "Ctrl+P"
            };
            quickOpen.Click += (s, args) => RunCommand(c => c.PbiBench_ShowQuickOpen());

            var semanticView = new ToolStripMenuItem
            {
                Name = "pbiBenchSemanticViewToolStripMenuItem",
                Text = "Semantic View",
                ShortcutKeyDisplayString = "Ctrl+Shift+P"
            };
            semanticView.Click += (s, args) => RunCommand(c => c.PbiBench_ShowSemanticView());

            pbiBench.DropDownItems.Add(quickOpen);
            pbiBench.DropDownItems.Add(semanticView);
            pbiBench.DropDownOpening += (s, args) =>
            {
                var controller = FormMain.Singleton?.UI;
                var modelAvailable = controller?.Handler?.Model != null;
                quickOpen.Enabled = modelAvailable;
                semanticView.Enabled = modelAvailable;
            };

            toolsMenu.DropDownItems.Add(new ToolStripSeparator());
            toolsMenu.DropDownItems.Add(pbiBench);
            Application.Idle -= EnsureMenuOnIdle;
        }

        private static void RunCommand(Action<UIController> command)
        {
            var controller = FormMain.Singleton?.UI;
            if (controller?.Handler?.Model == null) return;
            command(controller);
        }
    }
}
