using System.Windows.Forms;
using TabularEditor.PbiBench.Dax;
using TabularEditor.PbiBench.Navigation;
using TabularEditor.PbiBench.Semantic;

namespace TabularEditor.UI
{
    public partial class UIController
    {
        public void PbiBench_ShowProject()
        {
            using (var dialog = new TabularEditor.PbiBench.Project.PbiBenchProjectForm(Handler?.Model?.Name ?? "No model", Handler?.IsConnected == true))
                dialog.ShowDialog(UI.FormMain);
        }
        public void PbiBench_ShowRecipes()
        {
            if (Handler?.Model == null) return;
            using (var dialog = new TabularEditor.PbiBench.Automation.PbiBenchRecipeForm(() => Handler))
                dialog.ShowDialog(UI.FormMain);
        }
        public void PbiBench_ShowTablePreview()
        {
            if (Handler?.Model == null) return;
            var handler = Handler;
            using (var builder = new PbiBenchTablePreviewForm(Handler.Model))
            {
                if (builder.ShowDialog(UI.FormMain) != DialogResult.OK) return;
                if (!ReferenceEquals(handler, Handler) || !builder.IsCurrent()) return;
                PbiBenchAmoDaxQueryExecutor.TryCreate(Handler, out var executor, out var reason);
                using (var dialog = new PbiBenchDaxWorkbenchForm(executor, executor?.ConnectionLabel ?? "", reason))
                {
                    dialog.AuthoringModel = () => Handler?.Model;
                    dialog.NavigateToObject = obj => Goto(obj);
                    dialog.LoadPreviewDraft(builder.Query, builder.RowLimit);
                    dialog.ExecutionContextIsCurrent = () => ReferenceEquals(handler, Handler) && builder.IsCurrent();
                    dialog.ShowDialog(UI.FormMain);
                }
            }
        }
        // Installing the host from this partial keeps PbiBench commands out of FormMain.cs and
        // the legacy WinForms designer. It registers shortcuts and a small Tools > PbiBench menu.
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

        /// <summary>
        /// Opens a read-only dependency explorer backed by TE2's existing DAX dependency graph.
        /// The snapshot is rebuilt per invocation so stale TOM object references are not retained
        /// across model edits. Final navigation still goes through TE2's native Goto method.
        /// </summary>
        public void PbiBench_ShowSemanticView()
        {
            if (Handler?.Model == null) return;

            var snapshot = PbiBenchSemanticSnapshot.Build(Handler.Model);
            if (snapshot.Index.Descriptors.Count == 0)
            {
                UI.StatusLabel.Text = "PbiBench Semantic View: no semantic objects available.";
                return;
            }

            using (var dialog = new PbiBenchSemanticViewForm(snapshot))
            {
                if (dialog.ShowDialog(UI.FormMain) != DialogResult.OK) return;

                var selected = snapshot.Index.Resolve(dialog.SelectedId);
                if (selected == null || selected.IsRemoved)
                {
                    UI.StatusLabel.Text = "PbiBench Semantic View: selected object is no longer available.";
                    return;
                }

                Goto(selected);
                UI.StatusLabel.Text = "Semantic View: " + selected.Name;
            }
        }

        /// <summary>
        /// Opens the DAX Workbench. The editor/history UI is available for any loaded model, but
        /// Execute is enabled only when the current TE2 model exposes a live Analysis Services
        /// connection that can be cloned into an isolated query session.
        /// </summary>
        public void PbiBench_ShowDaxWorkbench()
        {
            if (Handler?.Model == null) return;

            PbiBenchAmoDaxQueryExecutor executor;
            string unavailableReason;
            PbiBenchAmoDaxQueryExecutor.TryCreate(Handler, out executor, out unavailableReason);

            using (var dialog = new PbiBenchDaxWorkbenchForm(
                executor,
                executor?.ConnectionLabel ?? string.Empty,
                unavailableReason))
            {
                dialog.AuthoringModel = () => Handler?.Model;
                dialog.NavigateToObject = obj => Goto(obj);
                dialog.ShowDialog(UI.FormMain);
            }
        }
    }
}
