using System;
using System.Linq;
using System.Windows.Forms;

namespace TabularEditor.PbiBench.Semantic
{
    /// <summary>
    /// Usage tab (S2 MODEL-002): measures no model expression references, and DAX references
    /// that do not resolve against the snapshot's fresh model names. Read-only; double-click or
    /// Enter navigates to the measure / the expression that contains the reference.
    /// </summary>
    internal sealed partial class PbiBenchSemanticViewForm
    {
        private TabPage _usageTab;
        private ListView _unusedMeasures;
        private ListView _unresolved;

        private void InitializeUsage()
        {
            var usage = _snapshot.Usage;
            _unusedMeasures = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
                MultiSelect = false, ShowItemToolTips = true, AccessibleName = "Measures not referenced by model expressions" };
            _unusedMeasures.Columns.Add("Measure", 220);
            _unusedMeasures.Columns.Add("Location", 300);
            _unusedMeasures.Columns.Add("Hidden", 60);
            foreach (var m in usage.UnusedMeasures)
            {
                var item = new ListViewItem(m.Measure.Name) { Tag = m.Measure.Id, ToolTipText = m.Measure.Path };
                item.SubItems.Add(m.Measure.Path);
                item.SubItems.Add(m.Measure.IsHidden ? "yes" : "");
                _unusedMeasures.Items.Add(item);
            }

            _unresolved = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false,
                MultiSelect = false, ShowItemToolTips = true, AccessibleName = "Unresolved DAX references" };
            foreach (var name in new[] { "Reference", "In object", "DAX property", "Code", "Advisory message" })
                _unresolved.Columns.Add(name, name == "Advisory message" ? 360 : name == "Code" ? 60 : 170);
            foreach (var u in usage.Unresolved)
            {
                var item = new ListViewItem(u.Reference.Length > 0 ? u.Reference : "?") { Tag = u.SourceId, ToolTipText = u.Message };
                item.SubItems.Add(u.SourcePath); item.SubItems.Add(u.Property); item.SubItems.Add(u.Code); item.SubItems.Add(u.Message);
                _unresolved.Items.Add(item);
            }
            _unusedMeasures.DoubleClick += (s, e) => AcceptNavigation(GetSelectedId(_unusedMeasures));
            _unresolved.DoubleClick += (s, e) => AcceptNavigation(GetSelectedId(_unresolved));
            _unusedMeasures.Enter += (s, e) => _lastUsageList = _unusedMeasures;
            _unresolved.Enter += (s, e) => _lastUsageList = _unresolved;

            // A proportional table layout (not a SplitContainer): hidden tab pages are laid out at
            // arbitrary sizes, and split-panel minima would clip there.
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(2, 6, 2, 4),
                Text = usage.UnusedMeasures.Count() + " measure(s) not referenced by any model expression. Report/visual usage is not known here." }, 0, 0);
            layout.Controls.Add(_unusedMeasures, 0, 1);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(2, 6, 2, 4),
                Text = usage.Unresolved.Count + (usage.UnresolvedTruncated ? "+" : "") + " unresolved reference(s) · advisory local check against current model names, not engine validation." }, 0, 2);
            layout.Controls.Add(_unresolved, 0, 3);
            _usageTab = new TabPage("Usage / Unresolved");
            _usageTab.Controls.Add(layout);
            _tabs.TabPages.Add(_usageTab);
            InitializeReportUsage(layout); // S6: "Used in report" column/summary (no-op without a report lineage)
        }

        private ListView _lastUsageList;

        private bool IsUsageTabSelected => _usageTab != null && ReferenceEquals(_tabs.SelectedTab, _usageTab);

        private string GetUsageSelectionId()
        {
            var preferred = _lastUsageList ?? (_unresolved.SelectedItems.Count > 0 ? _unresolved : _unusedMeasures);
            return GetSelectedId(preferred) ?? GetSelectedId(_unresolved) ?? GetSelectedId(_unusedMeasures);
        }

        /// <summary>Opens the form on the Usage tab (menu command).</summary>
        internal void SelectUsageTab() { if (_usageTab != null) _tabs.SelectedTab = _usageTab; }

        internal int UnusedMeasureRowCount => _unusedMeasures.Items.Count;
        internal int UnresolvedRowCount => _unresolved.Items.Count;

        /// <summary>Selects one Usage row and accepts navigation exactly as double-click does.</summary>
        internal string NavigateUsageRow(bool unresolved, int row)
        {
            var list = unresolved ? _unresolved : _unusedMeasures;
            if (row < 0 || row >= list.Items.Count) return null;
            SelectUsageTab();
            // ListView selection state lives in the native control; ensure it exists when hidden.
            if (!list.IsHandleCreated) { var handle = list.Handle; }
            list.Items[row].Selected = true;
            _lastUsageList = list;
            AcceptNavigation(GetPreferredNavigationId());
            return SelectedId;
        }
    }
}
