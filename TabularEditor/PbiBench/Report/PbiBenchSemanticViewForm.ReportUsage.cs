using System.Linq;
using System.Windows.Forms;

namespace TabularEditor.PbiBench.Semantic
{
    /// <summary>
    /// S6 additive hook into the S2 Usage tab: when the snapshot carries report counts (a report lineage
    /// computed against the same model names), add a "Used in report" column and say which measures the
    /// report keeps alive. Without counts the tab is unchanged.
    /// </summary>
    internal sealed partial class PbiBenchSemanticViewForm
    {
        internal bool ReportUsageKnown { get; private set; }
        internal string ReportUsageSummary { get; private set; } = "";

        private void InitializeReportUsage(TableLayoutPanel layout)
        {
            var usage = _snapshot.Usage;
            ReportUsageKnown = usage.Measures.Any(m => m.ExternalReferences.HasValue);
            if (!ReportUsageKnown) return;
            _unusedMeasures.Columns.Add("Used in report", 90);
            foreach (ListViewItem item in _unusedMeasures.Items) item.SubItems.Add("0");
            var reportOnly = usage.Measures.Count(m => m.IsUnusedInModel && (m.ExternalReferences ?? 0) > 0);
            var used = usage.Measures.Count(m => (m.ExternalReferences ?? 0) > 0);
            ReportUsageSummary = usage.UnusedMeasures.Count() + " measure(s) used neither by model expressions nor by report '" + (Report.PbiBenchReportUsage.Label ?? "?")
                + "'. " + used + " measure(s) are used in that report, " + reportOnly + " of them only there.";
            if (layout.GetControlFromPosition(0, 0) is Label label) label.Text = ReportUsageSummary;
        }

        /// <summary>Report uses shown for the given unused-measure row ("" when unknown).</summary>
        internal string ReportUsesForUnusedRow(int row) =>
            ReportUsageKnown && row >= 0 && row < _unusedMeasures.Items.Count && _unusedMeasures.Items[row].SubItems.Count > 3 ? _unusedMeasures.Items[row].SubItems[3].Text : "";
    }
}
