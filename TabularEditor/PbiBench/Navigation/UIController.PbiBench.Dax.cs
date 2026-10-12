using System.Windows.Forms;
using TabularEditor.PbiBench.Dax;
using TabularEditor.PbiBench.Semantic;

namespace TabularEditor.UI
{
    // S2 commands: opt-in expression-editor assistance and the model Usage view.
    public partial class UIController
    {
        /// <summary>
        /// Attaches/detaches local DAX completion and signature help on TE2's expression editor for
        /// this session. Only KeyDown events are used; the editor, its undo stack and TE2's
        /// accept/commit flow are unchanged.
        /// </summary>
        public void PbiBench_ToggleExpressionAuthoring()
        {
            if (PbiBenchExpressionEditorAuthoring.IsEnabled)
            {
                PbiBenchExpressionEditorAuthoring.Disable();
                UI.StatusLabel.Text = "Local DAX assistance off.";
                return;
            }
            if (UI.ExpressionEditor == null) return;
            PbiBenchExpressionEditorAuthoring.Enable(UI.ExpressionEditor, () => Handler?.Model, () => ExpressionEditor_IsDax,
                text => UI.StatusLabel.Text = text);
            UI.StatusLabel.Text = "Local DAX assistance on · Ctrl+Space complete · Ctrl+Shift+Space parameters · advisory, no network.";
        }

        public void PbiBench_CheckExpression()
        {
            if (!PbiBenchExpressionEditorAuthoring.IsEnabled) { UI.StatusLabel.Text = "Turn on local DAX assistance first (Tools > PbiBench)."; return; }
            PbiBenchExpressionEditorAuthoring.CheckCurrent((start, length) => {
                UI.ExpressionEditor.SelectionStart = start;
                UI.ExpressionEditor.SelectionLength = length;
                UI.ExpressionEditor.DoSelectionVisible();
            });
        }

        /// <summary>Semantic View opened on the Usage / Unresolved tab; navigation uses TE2 Goto.</summary>
        public void PbiBench_ShowModelUsage()
        {
            if (Handler?.Model == null) return;
            var snapshot = PbiBenchSemanticSnapshot.Build(Handler.Model);
            using (var dialog = new PbiBenchSemanticViewForm(snapshot))
            {
                dialog.SelectUsageTab();
                if (dialog.ShowDialog(UI.FormMain) != DialogResult.OK) return;
                var selected = snapshot.Index.Resolve(dialog.SelectedId);
                if (selected == null || selected.IsRemoved)
                {
                    UI.StatusLabel.Text = "PbiBench Usage: selected object is no longer available.";
                    return;
                }
                Goto(selected);
                UI.StatusLabel.Text = "Usage: " + selected.Name;
            }
        }
    }
}
