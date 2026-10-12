using System.Windows.Forms;
using TabularEditor.PbiBench.Dax;
using TabularEditor.PbiBench.Explore;
using TabularEditor.PbiBench.Optimize;

namespace TabularEditor.UI
{
    // S7 commands: bounded profiling drafts (EXP-002) and read-only model checks (OPT-001).
    public partial class UIController
    {
        /// <summary>
        /// Builds a profile/coverage draft and opens it as an unsaved Workbench document. The draft is
        /// never executed here; F5 in the Workbench runs it through the existing guarded query path,
        /// refused when the model handler or the profiled objects changed.
        /// </summary>
        public void PbiBench_ShowProfileDraft()
        {
            if (Handler?.Model == null) return;
            var handler = Handler;
            using (var builder = new PbiBenchProfileForm(Handler.Model))
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

        /// <summary>
        /// Curated BPA pack (via the existing analyzer's GetEffectiveRules additionalRules) plus semantic
        /// assertions. Read-only; navigation goes through TE2 Goto.
        /// </summary>
        public void PbiBench_ShowModelChecks()
        {
            if (Handler?.Model == null) return;
            var handler = Handler;
            var analyzer = UI.FormMain?.BPAForm?.Analyzer;
            using (var dialog = new PbiBenchAssertionsForm(
                (allowlist, includeUserRules) => PbiBenchModelChecks.Run(handler.Model, ReferenceEquals(analyzer?.Model, handler.Model) ? analyzer : null,
                    includeUserRules, allowlist),
                obj => { if (ReferenceEquals(handler, Handler) && !obj.IsRemoved) Goto(obj); },
                PbiBenchModelChecks.ReadAllowlistAnnotation(Handler.Model)))
            {
                dialog.ShowDialog(UI.FormMain);
            }
        }
    }
}
