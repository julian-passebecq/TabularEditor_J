using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PbiBench.Core.Project;
using PbiBench.Core.Serialization;

namespace TabularEditor.PbiBench.Project
{
    internal sealed class PbiBenchProjectForm : Form
    {
        private readonly TextBox _source = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox _details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false };
        private readonly ComboBox _projects = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button _scan = new Button { Text = "Refresh", AutoSize = true };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 40 };
        private readonly string _loadedIdentity;
        private readonly bool _live;
        private CancellationTokenSource _scanCts;
        private PbipProjectContext _context;
        private string _inventory = "";
        private readonly FlowLayoutPanel _commands;

        internal PbiBenchProjectForm(string loadedIdentity, bool live)
        {
            _loadedIdentity = loadedIdentity;
            _live = live;
            Text = "TE2 Enhanced · Project (read-only)";
            Size = new Size(1000, 650); MinimumSize = new Size(650, 400);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; AutoScaleMode = AutoScaleMode.Dpi;
            var commands = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, AutoScroll = true, WrapContents = false };
            _commands = commands;
            var folder = new Button { Text = "Choose folder…", AutoSize = true };
            folder.Click += (s, e) => { using (var dialog = new FolderBrowserDialog())
                if (dialog.ShowDialog(this) == DialogResult.OK) { _source.Text = dialog.SelectedPath; RefreshProject(); } };
            var file = new Button { Text = "Choose PBIP…", AutoSize = true };
            file.Click += (s, e) => { using (var dialog = new OpenFileDialog { Filter = "Power BI project|*.pbip" })
                if (dialog.ShowDialog(this) == DialogResult.OK) { _source.Text = dialog.FileName; RefreshProject(); } };
            _scan.Click += (s, e) => RefreshProject();
            var open = new Button { Text = "Open folder", AutoSize = true };
            open.Click += (s, e) => { if (_context == null) return;
                try { Process.Start(new ProcessStartInfo { FileName = _context.ProjectRoot, UseShellExecute = true }); }
                catch { _status.Text = "Could not open the project folder."; } };
            var export = new Button { Text = "Export context…", AutoSize = true };
            export.Click += (s, e) => Export();
            commands.Controls.AddRange(new Control[] { folder, file, _scan, open, export });
            var path = new Panel { Dock = DockStyle.Top, Height = 26 }; path.Controls.Add(_source);
            Controls.Add(_details); Controls.Add(_projects); Controls.Add(path); Controls.Add(commands); Controls.Add(_status);
            _projects.SelectedIndexChanged += (s, e) => {
                if (_context == null) return;
                _context.PbipFile = _projects.SelectedIndex <= 0 ? null : (string)_projects.SelectedItem;
                ShowInventory();
            };
            FormClosing += (s, e) => _scanCts?.Cancel();
            _status.Text = "Choose a folder or PBIP. Discovery never loads or changes the TE2 model.";
        }

        private async void RefreshProject()
        {
            if (_scanCts != null || string.IsNullOrWhiteSpace(_source.Text)) return;
            var source = _source.Text;
            _scanCts = new CancellationTokenSource();
            var cts = _scanCts;
            _scan.Enabled = false;
            _commands.Enabled = _source.Enabled = false;
            _context = null; _projects.Items.Clear(); _details.Clear();
            _status.Text = "Reading bounded project inventory…";
            try {
                var context = await Task.Run(() => PbipProjectDiscovery.Discover(source, cancellationToken: cts.Token));
                GitWorkingTree git = null;
                var gitStatus = "Git unavailable / no repository detected.";
                if (context.Git.Detected)
                {
                    try { git = await PbiBenchGitReader.ReadAsync(context.ProjectRoot, cts.Token); }
                    catch (Exception) { cts.Token.ThrowIfCancellationRequested(); gitStatus = "Git status unavailable (tool, timeout, access or output limit)."; }
                }
                if (IsDisposed || Disposing || cts.IsCancellationRequested) return;
                _context = context;
                if (git != null) {
                    context.Git.Branch = git.Branch;
                    context.Git.State = git.IsTruncated ? GitState.Unknown : git.Entries.Count == 0 ? GitState.Clean : GitState.Modified;
                    gitStatus = "Git: " + git.Branch + " · " + context.Git.State + (git.IsTruncated ? " (display truncated)" : "") + "\r\n" +
                        string.Join("\r\n", git.Entries.Select(x => x.Status + " " + Display(x.Path) +
                            (x.OriginalPath.Length > 0 ? " ← " + Display(x.OriginalPath) : "")));
                }
                _inventory = "Loaded: " + Display(_loadedIdentity) + "\r\nLive: " + (_live ? "Connected TE2 editing context; not compared" : "Not connected") +
                    "\r\nBaseline: not captured\r\nDisk inventory: " + context.ProjectRoot +
                    "\r\nDisk/Loaded/Live/Git identities are not equated. Refresh to inspect external changes.\r\n\r\n" + gitStatus +
                    "\r\n\r\nDiscovered semantic folders (association not inferred):\r\n" + string.Join("\r\n", context.SemanticModelFolders) +
                    "\r\n\r\nDiscovered report folders (association not inferred):\r\n" + string.Join("\r\n", context.ReportFolders);
                var selected = context.PbipFile;
                _projects.Items.Add("Select PBIP explicitly…");
                _projects.Items.AddRange(context.PbipCandidates);
                _projects.SelectedIndex = selected == null ? 0 : _projects.Items.IndexOf(selected);
                _status.Text = (context.DiscoveryIncomplete ? "Incomplete inventory · " : "Inventory captured · ") + string.Join(" ", context.DiscoveryDiagnostics);
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!IsDisposed) _status.Text = "Project could not be read. Check path and access."; }
            finally { cts.Dispose(); _scanCts = null; if (!IsDisposed) _commands.Enabled = _source.Enabled = _scan.Enabled = true; }
        }
        private void ShowInventory() => _details.Text = "Selected PBIP: " + (_context.PbipFile ?? "None") + "\r\n" + _inventory;
        private static string Display(string value) => (value ?? "").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        private void Export()
        {
            if (_context == null || _scanCts != null) return;
            using (var dialog = new SaveFileDialog { Filter = "JSON context|*.json", FileName = "te2-project-context.json", OverwritePrompt = true })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                try { ProjectContextSerializer.WriteFile(dialog.FileName, _context); _status.Text = "Exported paths/capabilities/Git summary only; no model contents or connection string."; }
                catch { _status.Text = "Context export failed. Check destination access."; }
            }
        }
    }
}
