using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using PbiBench.Core.Report;
using TabularEditor.PbiBench.Project;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Report
{
    /// <summary>
    /// REPORT-001: read-only PBIR report index and report → model lineage (resolved / unresolved / ambiguous)
    /// against the loaded TE2 model. Double-click or Enter on a row with a model object navigates there.
    /// REPORT-002 entry point: "Apply theme…" (only for writable schema versions) and journal restore.
    /// </summary>
    internal sealed class PbiBenchReportForm : Form
    {
        private readonly Func<Model> _model;
        private readonly string _journalRoot;
        private readonly TextBox _folder = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, AccessibleName = "Report folder" };
        private readonly Label _summary = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 92, Padding = new Padding(4) };
        private readonly ComboBox _filter = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, AccessibleName = "Lineage state filter" };
        private readonly ListView _lineage = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, ShowItemToolTips = true, AccessibleName = "Report to model lineage" };
        private readonly ListView _visuals = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, AccessibleName = "Report pages and visuals" };
        private readonly ListBox _diagnostics = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, AccessibleName = "Index diagnostics" };
        private readonly Button _applyTheme = new Button { Text = "Apply theme…", AutoSize = true, Enabled = false };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(4) };

        internal PbiBenchReportForm(Func<Model> model, string initialFolder, string journalRoot)
        {
            _model = model ?? (() => null);
            _journalRoot = journalRoot;
            Text = "TE2 Enhanced · Report (PBIR) index and lineage";
            Size = new Size(1100, 680); MinimumSize = new Size(720, 420);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi; KeyPreview = true;

            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 36, ColumnCount = 4, Padding = new Padding(4) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var browse = new Button { Text = "Browse…", AutoSize = true };
            var refresh = new Button { Text = "Index", AutoSize = true };
            top.Controls.Add(new Label { Text = "Report folder (X.Report):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            top.Controls.Add(_folder, 1, 0); top.Controls.Add(browse, 2, 0); top.Controls.Add(refresh, 3, 0);

            foreach (var name in new[] { "State", "Report field", "Kind", "Uses", "Visuals", "Model object / reason", "First location" })
                _lineage.Columns.Add(name, name == "Model object / reason" ? 330 : name == "Report field" ? 220 : name == "First location" ? 260 : name == "State" ? 90 : 60);
            foreach (var name in new[] { "Page", "Visual", "Type", "Fields", "Schema" })
                _visuals.Columns.Add(name, name == "Schema" ? 220 : 150);
            _filter.Items.AddRange(new object[] { "All states", "Unresolved", "Ambiguous", "Resolved" });
            _filter.SelectedIndex = 0;

            var lineagePanel = new Panel { Dock = DockStyle.Fill };
            var filterBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32 };
            filterBar.Controls.Add(new Label { Text = "Show:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            filterBar.Controls.Add(_filter);
            filterBar.Controls.Add(new Label { AutoSize = true, Padding = new Padding(8, 6, 0, 0),
                Text = "Names only (case-insensitive) against the loaded model; no engine query. Ambiguous = cannot be decided from names." });
            lineagePanel.Controls.Add(_lineage); lineagePanel.Controls.Add(filterBar);

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var lineageTab = new TabPage("Lineage"); lineageTab.Controls.Add(lineagePanel);
            var visualsTab = new TabPage("Pages and visuals"); visualsTab.Controls.Add(_visuals);
            var diagnosticsTab = new TabPage("Diagnostics"); diagnosticsTab.Controls.Add(_diagnostics);
            tabs.TabPages.Add(lineageTab); tabs.TabPages.Add(visualsTab); tabs.TabPages.Add(diagnosticsTab);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            var journals = new Button { Text = "Restore journal…", AutoSize = true };
            buttons.Controls.Add(close); buttons.Controls.Add(journals); buttons.Controls.Add(_applyTheme);
            CancelButton = close;

            Controls.Add(tabs); Controls.Add(_summary); Controls.Add(top); Controls.Add(_status); Controls.Add(buttons);

            browse.Click += (s, e) =>
            {
                using (var dialog = new FolderBrowserDialog { Description = "Choose a PBIR report folder (X.Report)", ShowNewFolderButton = false, SelectedPath = _folder.Text })
                    if (dialog.ShowDialog(this) == DialogResult.OK) LoadFolder(dialog.SelectedPath);
            };
            refresh.Click += (s, e) => LoadFolder(_folder.Text);
            _filter.SelectedIndexChanged += (s, e) => FillLineage();
            _lineage.DoubleClick += (s, e) => AcceptSelection();
            _lineage.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; AcceptSelection(); } };
            _applyTheme.Click += (s, e) => ShowThemeApply();
            journals.Click += (s, e) => { using (var dialog = new PbiBenchJournalRestoreForm(_journalRoot)) dialog.ShowDialog(this); };

            _folder.Text = initialFolder ?? "";
            if (!string.IsNullOrWhiteSpace(initialFolder)) LoadFolder(initialFolder);
            else _status.Text = "Choose a report folder. PBIP reports next to the loaded model's X.SemanticModel folder are found automatically.";
        }

        internal PbirReportIndex Index { get; private set; }
        internal ReportLineage Lineage { get; private set; }
        internal ITabularNamedObject SelectedObject { get; private set; }
        internal string StatusText => _status.Text;
        internal string SummaryText => _summary.Text;
        internal int LineageRowCount => _lineage.Items.Count;
        internal int VisualRowCount => _visuals.Items.Count;
        internal int DiagnosticCount => _diagnostics.Items.Count;
        internal bool CanApplyTheme => _applyTheme.Enabled;
        /// <summary>Test seam: theme dialog factory (default shows the dialog modally).</summary>
        internal Func<PbirReportIndex, DialogResult> ThemeDialog { get; set; }

        internal void SetFilter(ReportLineageState? state) => _filter.SelectedIndex = state == null ? 0 : state == ReportLineageState.Unresolved ? 1 : state == ReportLineageState.Ambiguous ? 2 : 3;

        internal void LoadFolder(string folder)
        {
            Index = null; Lineage = null; _applyTheme.Enabled = false;
            _lineage.Items.Clear(); _visuals.Items.Clear(); _diagnostics.Items.Clear();
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) { _summary.Text = ""; _status.Text = "Report folder not found."; return; }
            _folder.Text = folder;
            try { Index = PbirReportIndex.Load(folder); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            { _summary.Text = ""; _status.Text = "Report could not be indexed: " + ex.Message; return; }
            var model = _model();
            Lineage = PbiBenchReportBinder.BuildLineage(Index, model);
            if (model != null) PbiBenchReportUsage.Attach(Lineage, Path.GetFileName(Index.ReportFolder));
            foreach (var d in Index.Diagnostics) _diagnostics.Items.Add(d);
            foreach (var page in Index.Pages)
                foreach (var visual in page.Visuals)
                    _visuals.Items.Add(new ListViewItem(new[] { page.DisplayName, visual.Name, visual.VisualType, visual.Fields.Count.ToString(), visual.Schema.Describe() }));
            FillLineage();
            _summary.Text = Describe(Index, Lineage, model != null);
            _applyTheme.Enabled = Index.IsWritable;
            _status.Text = model == null ? "No model loaded: every field is unresolved. Open the semantic model in TE2 for lineage."
                : "Lineage bound to the loaded model; Usage tab shows report counts until the model names change.";
        }

        private static string Describe(PbirReportIndex index, ReportLineage lineage, bool hasModel)
        {
            var sb = new StringBuilder();
            sb.Append(index.Format == PbirReportFormat.Pbir ? "PBIR " + (index.FormatVersion ?? "?") : index.Format.ToString())
              .Append(" · report.json ").Append(index.ReportSchema.Describe())
              .Append(" · semantic model ").Append(index.SemanticModelPath ?? index.SemanticModelConnection ?? "unknown").AppendLine();
            sb.Append(index.Pages.Count).Append(" page(s), ").Append(index.VisualCount).Append(" visual(s), ").Append(index.AllFields.Count()).Append(" field use(s)")
              .Append(index.Truncated ? " · TRUNCATED at index limits" : "").Append(index.Diagnostics.Count > 0 ? " · " + index.Diagnostics.Count + " diagnostic(s)" : "").AppendLine();
            sb.Append("Theme: base ").Append(index.Theme.BaseThemeName ?? "none").Append(", custom ").Append(index.Theme.CustomThemeName ?? "none")
              .Append(index.Theme.CustomThemeName != null && !index.Theme.CustomThemeFileExists ? " (file missing)" : "").AppendLine();
            sb.Append(index.IsWritable ? "Writable: theme apply available (reviewed, journaled)." : "READ-ONLY: " + index.ReadOnlyReason);
            if (hasModel) sb.Append(" · Lineage: ").Append(lineage.Count(ReportLineageState.Resolved)).Append(" resolved, ")
                .Append(lineage.Count(ReportLineageState.Unresolved)).Append(" unresolved, ").Append(lineage.Count(ReportLineageState.Ambiguous)).Append(" ambiguous");
            return sb.ToString();
        }

        private void FillLineage()
        {
            _lineage.BeginUpdate();
            _lineage.Items.Clear();
            var filter = _filter.SelectedIndex;
            foreach (var entry in Lineage?.Entries ?? Array.Empty<ReportLineageEntry>())
            {
                if (filter == 1 && entry.State != ReportLineageState.Unresolved) continue;
                if (filter == 2 && entry.State != ReportLineageState.Ambiguous) continue;
                if (filter == 3 && entry.State != ReportLineageState.Resolved) continue;
                var target = entry.ModelName != null ? "'" + entry.ModelTable + "'[" + entry.ModelName + "]" : "";
                var reason = entry.Reason.Length == 0 ? target : target.Length == 0 ? entry.Reason : target + " · " + entry.Reason;
                var item = new ListViewItem(new[] { entry.State.ToString(), entry.Display, entry.Kind.ToString(), entry.Uses.ToString(), entry.Visuals.ToString(), reason, entry.FirstLocation })
                { Tag = entry, ToolTipText = string.Join(Environment.NewLine, entry.Locations.Take(10).Select(ReportLineageEntry.Describe)) };
                if (entry.State == ReportLineageState.Unresolved) item.ForeColor = Color.Firebrick;
                else if (entry.State == ReportLineageState.Ambiguous) item.ForeColor = Color.DarkGoldenrod;
                _lineage.Items.Add(item);
            }
            _lineage.EndUpdate();
        }

        /// <summary>Selects a lineage row and accepts it as double-click does; returns the model object or null.</summary>
        internal ITabularNamedObject NavigateLineageRow(int row)
        {
            if (row < 0 || row >= _lineage.Items.Count) return null;
            if (!_lineage.IsHandleCreated) { var handle = _lineage.Handle; }
            _lineage.Items[row].Selected = true;
            AcceptSelection();
            return SelectedObject;
        }

        internal int FindLineageRow(Func<ReportLineageEntry, bool> predicate)
        {
            for (var i = 0; i < _lineage.Items.Count; i++) if (predicate((ReportLineageEntry)_lineage.Items[i].Tag)) return i;
            return -1;
        }

        private void AcceptSelection()
        {
            if (_lineage.SelectedItems.Count == 0 || !(_lineage.SelectedItems[0].Tag is ReportLineageEntry entry)) return;
            var target = PbiBenchReportBinder.Resolve(_model(), entry);
            if (target == null || target.IsRemoved) { _status.Text = "No model object to navigate to for " + entry.Display + "."; return; }
            SelectedObject = target;
            DialogResult = DialogResult.OK;
        }

        private void ShowThemeApply()
        {
            if (Index == null || !Index.IsWritable) return;
            var result = ThemeDialog != null ? ThemeDialog(Index) : ShowThemeDialog();
            // Files may have changed: re-index so the next review starts from disk.
            LoadFolder(Index.ReportFolder);
            if (result == DialogResult.OK) _status.Text = "Theme applied; report re-indexed. Restore journal… undoes it exactly.";
        }

        private DialogResult ShowThemeDialog()
        {
            using (var dialog = new PbiBenchThemeApplyForm(Index, _journalRoot))
            {
                dialog.ShowDialog(this);
                return dialog.Applied ? DialogResult.OK : DialogResult.Cancel;
            }
        }
    }
}
