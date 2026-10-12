using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Project;

namespace TabularEditor.PbiBench.Project
{
    internal static class PbiBenchProjectPaths
    {
        /// <summary>Per-user journal folder for reviewed file plans (never inside a project).</summary>
        internal static string JournalRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TE2Enhanced", "journal");
    }

    /// <summary>
    /// Read-only review of external disk changes against an accepted baseline. The only action is
    /// to accept the current disk state as the new baseline (DialogResult.Yes); nothing is written
    /// to the project and the TE2 model is never reloaded or changed from here.
    /// </summary>
    internal sealed class PbiBenchSyncReviewForm : Form
    {
        private readonly ListView _list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };

        internal PbiBenchSyncReviewForm(string title, IEnumerable<string> identityLines, ExternalChangeSet changes, string message, bool allowAccept)
        {
            Text = "TE2 Enhanced · " + title;
            Size = new Size(900, 560); MinimumSize = new Size(560, 360);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var header = new TextBox { Dock = DockStyle.Top, Multiline = true, ReadOnly = true, Height = 110, ScrollBars = ScrollBars.Vertical,
                Text = string.Join("\r\n", (identityLines ?? Enumerable.Empty<string>()).Concat(new[] { "", message ?? "" })) };
            _list.Columns.Add("Change", 100); _list.Columns.Add("Path", 480); _list.Columns.Add("Previous path", 260);
            if (changes != null)
                foreach (var change in changes.Changes.Take(2000))
                    _list.Items.Add(new ListViewItem(new[] { change.Kind.ToString(), change.Path, change.PreviousPath ?? "" }));
            if (changes != null && changes.Changes.Count > 2000) _list.Items.Add(new ListViewItem(new[] { "…", "Display limited to 2000 changes", "" }));
            if (changes != null && changes.Incomplete) _list.Items.Add(new ListViewItem(new[] { "Note", "Manifest was bounded/incomplete; absence of a change is not proof.", "" }));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(close);
            if (allowAccept)
            {
                var accept = new Button { Text = "Accept disk as new baseline", AutoSize = true, DialogResult = DialogResult.Yes };
                buttons.Controls.Add(accept);
            }
            CancelButton = close;
            Controls.Add(_list); Controls.Add(header); Controls.Add(buttons);
        }

        internal int RowCount => _list.Items.Count;
    }

    /// <summary>Explicit restore of a reviewed-file-plan journal: preview first, refuse stale, confirm, restore.</summary>
    internal sealed class PbiBenchJournalRestoreForm : Form
    {
        private readonly ListBox _journals = new ListBox { Dock = DockStyle.Left, Width = 320, IntegralHeight = false };
        private readonly ListView _preview = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };
        private readonly Button _restore = new Button { Text = "Restore selected…", AutoSize = true, Enabled = false };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 36 };
        private readonly string _root;
        private readonly Func<string, DialogResult> _confirm;

        internal PbiBenchJournalRestoreForm(string journalRoot, Func<string, DialogResult> confirm = null)
        {
            _root = journalRoot;
            _confirm = confirm ?? (text => MessageBox.Show(this, text, "Restore files", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning));
            Text = "TE2 Enhanced · Restore reviewed file plan";
            Size = new Size(960, 520); MinimumSize = new Size(620, 340);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            _preview.Columns.Add("Path", 360); _preview.Columns.Add("Plan action", 90); _preview.Columns.Add("Restore", 260);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(close); buttons.Controls.Add(_restore);
            CancelButton = close;
            Controls.Add(_preview); Controls.Add(_journals); Controls.Add(_status); Controls.Add(buttons);
            _journals.SelectedIndexChanged += (s, e) => ShowPreview();
            _restore.Click += (s, e) => RestoreSelected();
            Reload();
        }

        internal int JournalCount => _journals.Items.Count;
        internal void Select(int index) { _journals.SelectedIndex = index; }
        internal bool CanRestore => _restore.Enabled;
        internal string StatusText => _status.Text;
        internal void RestoreSelected() => DoRestore();

        private void Reload()
        {
            _journals.Items.Clear();
            try
            {
                foreach (var path in ReviewedFilePlan.ListJournals(_root))
                {
                    try { var j = ReviewedFilePlan.ReadJournal(path); _journals.Items.Add(new JournalItem(path, j)); }
                    catch (Exception) { /* unreadable journals are skipped, never deleted */ }
                }
                _status.Text = _journals.Items.Count == 0 ? "No journals in " + _root : "Journals are kept in " + _root + ". Select one to preview.";
            }
            catch (Exception) { _status.Text = "Journal folder could not be read."; }
        }

        private void ShowPreview()
        {
            _preview.Items.Clear(); _restore.Enabled = false;
            if (!(_journals.SelectedItem is JournalItem item)) return;
            try
            {
                var rows = ReviewedFilePlan.PreviewRestore(item.Path);
                foreach (var row in rows)
                    _preview.Items.Add(new ListViewItem(new[] { row.RelativePath, row.Kind.ToString(), row.IsStale ? "BLOCKED: " + row.StaleReason : "Restorable" }));
                var restorable = item.Journal.State != ReviewedFileJournalState.Restored && item.Journal.State != ReviewedFileJournalState.RolledBack;
                _restore.Enabled = restorable && rows.Count > 0 && rows.All(r => !r.IsStale);
                _status.Text = item.Journal.Label + " · " + item.Journal.State + " · root " + item.Journal.Root +
                    (!restorable ? " · already restored" : rows.Any(r => r.IsStale) ? " · files changed after apply; restore refused" : "");
            }
            catch (Exception) { _status.Text = "Journal could not be previewed."; }
        }

        private void DoRestore()
        {
            if (!(_journals.SelectedItem is JournalItem item) || !_restore.Enabled) return;
            if (_confirm("Restore " + _preview.Items.Count + " file(s) under " + item.Journal.Root + " to their state before '" + item.Journal.Label +
                "'? Files changed since are refused. The TE2 model is not reloaded.") != DialogResult.OK) return;
            string message;
            try { var count = ReviewedFilePlan.Restore(item.Path); message = "Restored " + count + " file(s). Reload in TE2 if the model came from these files."; }
            catch (ReviewedFilePlanStaleException ex) { message = ex.Message; }
            catch (Exception) { message = "Restore failed; the journal and backups were kept. Check access and try again explicitly."; }
            var index = _journals.SelectedIndex;
            Reload();
            if (index >= 0 && index < _journals.Items.Count) _journals.SelectedIndex = index;
            _status.Text = message;
        }

        private sealed class JournalItem
        {
            internal JournalItem(string path, ReviewedFileJournal journal) { Path = path; Journal = journal; }
            internal string Path { get; }
            internal ReviewedFileJournal Journal { get; }
            public override string ToString() => Journal.CreatedUtc.Substring(0, Math.Min(16, Journal.CreatedUtc.Length)).Replace('T', ' ') + " · " + Journal.State + " · " + Journal.Label;
        }
    }
}
