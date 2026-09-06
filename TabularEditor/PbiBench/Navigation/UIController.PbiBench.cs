using System.Windows.Forms;
using TabularEditor.PbiBench.Navigation;

namespace TabularEditor.UI
{
    public partial class UIController
    {
        // Installing the filter from this partial keeps the Ctrl+P host hook out of FormMain.cs.
        // UIController is created on the WinForms UI thread, so the filter is registered on the
        // same thread that owns the TE2 message pump.
        private static readonly PbiBenchShortcutFilter PbiBenchShortcutFilterRegistration =
            PbiBenchShortcutFilter.Install();

        /// <summary>
        /// Builds a fresh read-only semantic index, presents Quick Open, and delegates final
        /// navigation to TE2's existing Goto implementation so tree/filter visibility rules
        /// remain owned by the upstream UI controller.
        /// </summary>
        public void PbiBench_ShowQuickOpen()
        {
            if (Handler?.Model == null) return;

            var index = PbiBenchQuickOpenIndex.Build(Handler.Model);
            if (index.Descriptors.Count == 0)
            {
                UI.StatusLabel.Text = "PbiBench Quick Open: no searchable semantic objects.";
                return;
            }

            using (var dialog = new PbiBenchQuickOpenForm(index.Descriptors))
            {
                if (dialog.ShowDialog(UI.FormMain) != DialogResult.OK) return;

                var selected = index.Resolve(dialog.SelectedId);
                if (selected == null || selected.IsRemoved)
                {
                    UI.StatusLabel.Text = "PbiBench Quick Open: selected object is no longer available.";
                    return;
                }

                Goto(selected);
                UI.StatusLabel.Text = "Quick Open: " + selected.Name;
            }
        }
    }
}
