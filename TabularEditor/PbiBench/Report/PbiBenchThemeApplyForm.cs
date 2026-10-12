using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Project;
using PbiBench.Core.Report;

namespace TabularEditor.PbiBench.Report
{
    /// <summary>
    /// REPORT-002 reviewed theme apply: choose a theme JSON, review every file change (hashes, sizes,
    /// themeCollection before/after), confirm, apply through ReviewedFilePlan with a per-user journal,
    /// and restore the exact previous bytes from that journal. The report must be a writable schema version.
    /// </summary>
    internal sealed class PbiBenchThemeApplyForm : Form
    {
        private readonly PbirReportIndex _index;
        private readonly string _journalRoot;
        private readonly Func<string, DialogResult> _confirm;
        private readonly Label _theme = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "No theme chosen.", TextAlign = ContentAlignment.MiddleLeft };
        private readonly ListView _changes = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, AccessibleName = "Planned file changes" };
        private readonly TextBox _before = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f), AccessibleName = "themeCollection before" };
        private readonly TextBox _after = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 9f), AccessibleName = "themeCollection after" };
        private readonly Button _apply = new Button { Text = "Apply…", AutoSize = true, Enabled = false };
        private readonly Button _restore = new Button { Text = "Restore this apply…", AutoSize = true, Enabled = false };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(4) };
        private ThemeApplyPlan _plan;
        private string _journal;

        internal PbiBenchThemeApplyForm(PbirReportIndex index, string journalRoot, Func<string, DialogResult> confirm = null)
        {
            _index = index ?? throw new ArgumentNullException(nameof(index));
            _journalRoot = journalRoot;
            _confirm = confirm ?? (text => MessageBox.Show(this, text, "Apply report theme", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2));
            Text = "TE2 Enhanced · Apply report theme (reviewed)";
            Size = new Size(1000, 640); MinimumSize = new Size(680, 420);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;

            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 2, Padding = new Padding(4) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var choose = new Button { Text = "Choose theme JSON…", AutoSize = true };
            top.Controls.Add(choose, 0, 0); top.Controls.Add(_theme, 1, 0);

            foreach (var name in new[] { "Change", "File (relative to report folder)", "Current SHA-256", "After SHA-256", "Bytes now → after" })
                _changes.Columns.Add(name, name.StartsWith("File") ? 330 : name == "Change" ? 70 : 150);
            var snippets = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            snippets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50)); snippets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            snippets.RowStyles.Add(new RowStyle(SizeType.AutoSize)); snippets.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            snippets.Controls.Add(new Label { Text = "report.json themeCollection now", AutoSize = true }, 0, 0);
            snippets.Controls.Add(new Label { Text = "after apply (other report.json text unchanged)", AutoSize = true }, 1, 0);
            snippets.Controls.Add(_before, 0, 1); snippets.Controls.Add(_after, 1, 1);
            var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 35)); body.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            body.Controls.Add(_changes, 0, 0); body.Controls.Add(snippets, 0, 1);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(close); buttons.Controls.Add(_restore); buttons.Controls.Add(_apply);
            CancelButton = close;
            Controls.Add(body); Controls.Add(top); Controls.Add(_status); Controls.Add(buttons);

            choose.Click += (s, e) =>
            {
                using (var dialog = new OpenFileDialog { Filter = "Power BI theme (*.json)|*.json", Title = "Choose a report theme JSON" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) LoadThemeFile(dialog.FileName);
            };
            _apply.Click += (s, e) => Apply();
            _restore.Click += (s, e) => RestoreLast();
            _status.Text = index.IsWritable ? "Report " + Path.GetFileName(index.ReportFolder) + " · writes are reviewed and journaled under " + journalRoot
                : "READ-ONLY report: " + index.ReadOnlyReason;
        }

        internal bool Applied { get; private set; }
        internal ReviewedFilePlanApplyException LastApplyFailure { get; private set; }
        internal bool CanApply => _apply.Enabled;
        internal bool CanRestore => _restore.Enabled;
        internal string StatusText => _status.Text;
        internal int ChangeRowCount => _changes.Items.Count;
        internal string AfterText => _after.Text;
        internal string JournalPath => _journal;

        internal void LoadThemeFile(string path)
        {
            byte[] bytes;
            try
            {
                if (new FileInfo(path).Length > ThemeApplyPlan.MaxThemeBytes) { Refuse("Theme file is larger than " + ThemeApplyPlan.MaxThemeBytes + " bytes."); return; }
                bytes = File.ReadAllBytes(path);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { Refuse("Theme file could not be read: " + ex.Message); return; }
            LoadTheme(bytes, path);
        }

        internal void LoadTheme(byte[] bytes, string label)
        {
            _plan = null; _apply.Enabled = false; _changes.Items.Clear(); _before.Text = ""; _after.Text = "";
            _theme.Text = label ?? "";
            try { _plan = ThemeApplyPlan.Create(_index, bytes); }
            catch (ThemeApplyRefusedException ex) { Refuse(ex.Message); return; }
            catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException) { Refuse("Plan could not be built: " + ex.Message); return; }
            var preview = _plan.Plan.Preview();
            foreach (var row in preview)
                _changes.Items.Add(new ListViewItem(new[] { row.Kind.ToString(), row.RelativePath, Short(row.CurrentHash), Short(row.AfterHash),
                    (row.CurrentLength?.ToString() ?? "-") + " → " + (row.AfterLength?.ToString() ?? "-") }) { ForeColor = row.IsStale ? Color.Firebrick : SystemColors.WindowText });
            _before.Text = _plan.BeforeSnippet.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            _after.Text = _plan.AfterSnippet.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
            var stale = preview.Where(p => p.IsStale).ToArray();
            _apply.Enabled = stale.Length == 0;
            _status.Text = stale.Length > 0 ? "Stale: " + stale[0].RelativePath + " — " + stale[0].StaleReason + " Close and re-index the report."
                : "Theme '" + _plan.ThemeName + "' → " + _plan.ThemeFileName + ". Review the " + preview.Count + " change(s), then Apply.";
        }

        private void Refuse(string message) { _plan = null; _apply.Enabled = false; _status.Text = "Refused: " + message; }

        private static string Short(string hash) => string.IsNullOrEmpty(hash) ? "-" : hash.Substring(0, Math.Min(12, hash.Length));

        internal void Apply()
        {
            if (_plan == null || !_apply.Enabled) return;
            if (_confirm("Write " + _plan.Plan.Changes.Count + " file(s) under " + _index.ReportFolder + "?\n\nA journal with backups is kept in " + _journalRoot +
                " so the previous bytes can be restored exactly. Power BI Desktop must reopen the report to show the theme.") != DialogResult.OK) return;
            try
            {
                var result = _plan.Plan.Apply(_journalRoot);
                _journal = result.JournalPath;
                Applied = true;
                _apply.Enabled = false;
                _restore.Enabled = true;
                _status.Text = "Applied: " + result.FilesWritten + " file(s) written. Journal " + result.JournalPath + ".";
            }
            catch (ReviewedFilePlanStaleException ex)
            {
                _apply.Enabled = false;
                _status.Text = "Refused, nothing written: " + string.Join("; ", ex.Stale.Select(s => s.RelativePath + " — " + s.StaleReason)) + " Re-index the report and review again.";
            }
            catch (ReviewedFilePlanApplyException ex)
            {
                _apply.Enabled = false;
                _status.Text = (ex.RolledBack ? "Apply failed and was rolled back: " : "Apply failed; rollback incomplete, use Restore journal: ") + ex.Message
                    + (ex.InnerException != null ? " (" + ex.InnerException.Message.Trim() + ") Nothing is retried automatically; close any program holding the file and apply again." : "");
                LastApplyFailure = ex;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                _apply.Enabled = false;
                _status.Text = "Apply refused: " + ex.Message;
            }
        }

        internal void RestoreLast()
        {
            if (_journal == null) return;
            if (_confirm("Restore the files changed by this theme apply to their exact previous bytes? Files changed since are refused.") != DialogResult.OK) return;
            try
            {
                var count = ReviewedFilePlan.Restore(_journal);
                _restore.Enabled = false;
                _status.Text = "Restored " + count + " file(s) to their state before the theme apply.";
            }
            catch (ReviewedFilePlanStaleException ex) { _status.Text = "Restore refused: " + ex.Message; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException) { _status.Text = "Restore failed; journal kept: " + ex.Message; }
        }
    }
}
