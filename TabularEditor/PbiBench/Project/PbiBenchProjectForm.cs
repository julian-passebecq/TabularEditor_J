using System;
using System.Collections.Generic;
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
    /// <summary>
    /// Project window: bounded inventory, stable local identity, distinct Disk/Baseline/Loaded/Live/Git
    /// stamps, debounced external-change banner, VS Code handoff, journal restore and (when the host
    /// supplies it) a save-target check for the loaded model. Project files are never written here;
    /// the per-user recent-project registry and journals live under %LOCALAPPDATA%\TE2Enhanced.
    /// </summary>
    internal sealed class PbiBenchProjectForm : Form
    {
        private readonly TextBox _source = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox _details = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false };
        private readonly ComboBox _projects = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly Button _scan = new Button { Text = "Refresh", AutoSize = true };
        private readonly Label _status = new Label { Dock = DockStyle.Bottom, Height = 40 };
        private readonly Panel _banner = new Panel { Dock = DockStyle.Top, Height = 30, Visible = false, BackColor = Color.LightGoldenrodYellow };
        private readonly Label _bannerText = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly Button _vsCode = new Button { Text = "Open in VS Code", AutoSize = true, Enabled = false };
        private readonly Button _review = new Button { Text = "Review changes…", AutoSize = true, Enabled = false };
        private readonly Button _saveCheck = new Button { Text = "Check save target…", AutoSize = true, Visible = false };
        private readonly string _loadedIdentity;
        private readonly bool _live;
        private CancellationTokenSource _scanCts;
        private PbipProjectContext _context;
        private string _inventory = "";
        private string _identityText = "";
        private readonly FlowLayoutPanel _commands;
        private PbiBenchProjectWatcher _watcher;
        private BaselineStamp _baseline;
        private DiskStamp _disk;
        private GitStamp _git;
        private int _generation;
        private bool _rescanning, _rescanPending;

        internal PbiBenchProjectForm(string loadedIdentity, bool live)
        {
            _loadedIdentity = loadedIdentity;
            _live = live;
            Text = "TE2 Enhanced · Project";
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
            var recent = new Button { Text = "Recent ▾", AutoSize = true };
            recent.Click += (s, e) => ShowRecent(recent);
            _scan.Click += (s, e) => RefreshProject();
            var open = new Button { Text = "Open folder", AutoSize = true };
            open.Click += (s, e) => { if (_context == null) return;
                try { Process.Start(new ProcessStartInfo { FileName = _context.ProjectRoot, UseShellExecute = true }); }
                catch { _status.Text = "Could not open the project folder."; } };
            _vsCode.Click += (s, e) => { if (_context == null) return;
                _status.Text = PbiBenchVsCodeLauncher.Open(_context.ProjectRoot) ?? "VS Code started for " + _context.ProjectRoot + " (no shell, argument list only)."; };
            _review.Click += (s, e) => ReviewChanges();
            var journals = new Button { Text = "Restore journal…", AutoSize = true };
            journals.Click += (s, e) => { using (var dialog = new PbiBenchJournalRestoreForm(PbiBenchProjectPaths.JournalRoot)) dialog.ShowDialog(this); };
            _saveCheck.Click += (s, e) => ReviewSaveTarget();
            var export = new Button { Text = "Export context…", AutoSize = true };
            export.Click += (s, e) => Export();
            commands.Controls.AddRange(new Control[] { folder, file, recent, _scan, open, _vsCode, _review, _saveCheck, journals, export });
            var bannerReview = new Button { Text = "Review…", AutoSize = true, Dock = DockStyle.Right };
            bannerReview.Click += (s, e) => ReviewChanges();
            _banner.Controls.Add(_bannerText); _banner.Controls.Add(bannerReview);
            var path = new Panel { Dock = DockStyle.Top, Height = 26 }; path.Controls.Add(_source);
            Controls.Add(_details); Controls.Add(_projects); Controls.Add(_banner); Controls.Add(path); Controls.Add(commands); Controls.Add(_status);
            _projects.SelectedIndexChanged += (s, e) => {
                if (_context == null) return;
                _context.PbipFile = _projects.SelectedIndex <= 0 ? null : (string)_projects.SelectedItem;
                UpdateIdentity();
                ShowInventory();
            };
            FormClosing += (s, e) => { _scanCts?.Cancel(); StopWatcher(); };
            _status.Text = "Choose a folder or PBIP. Discovery never loads or changes the TE2 model.";
        }

        /// <summary>Loaded-model stamp supplied by the host (TE2 in-memory state; never a disk hash).</summary>
        internal Func<LoadedStamp> LoadedState { get; set; }
        /// <summary>Optional save-target check for the loaded model (host supplied).</summary>
        internal Func<SaveTargetCheck> CheckSaveTarget { get => _checkSaveTarget; set { _checkSaveTarget = value; _saveCheck.Visible = value != null; } }
        private Func<SaveTargetCheck> _checkSaveTarget;
        internal Action AcceptSaveTargetBaseline { get; set; }
        internal string RegistryPath { get; set; } = ProjectRegistry.DefaultPath();
        internal TimeSpan WatchDebounce { get; set; } = TimeSpan.FromMilliseconds(750);
        internal ProjectIdentity Identity { get; private set; }
        internal ExternalChangeSet LastChanges { get; private set; }
        internal int WatchSignals { get; private set; }
        internal string BannerText => _banner.Visible ? _bannerText.Text : "";
        internal string StatusText => _status.Text;
        internal string DetailsText => _details.Text;
        internal string SourcePath { get => _source.Text; set => _source.Text = value; }

        private void RefreshProject() { var _ = RefreshProjectAsync(); }

        internal async Task RefreshProjectAsync()
        {
            if (_scanCts != null || string.IsNullOrWhiteSpace(_source.Text)) return;
            var source = _source.Text;
            _scanCts = new CancellationTokenSource();
            var cts = _scanCts;
            var generation = ++_generation;
            StopWatcher();
            _scan.Enabled = false;
            _commands.Enabled = _source.Enabled = false;
            _context = null; _projects.Items.Clear(); _details.Clear(); _banner.Visible = false;
            _baseline = null; _disk = null; Identity = null; LastChanges = null;
            _status.Text = "Reading bounded project inventory…";
            try {
                var context = await Task.Run(() => PbipProjectDiscovery.Discover(source, cancellationToken: cts.Token));
                var manifest = await Task.Run(() => FileManifest.Capture(context.ProjectRoot, cts.Token));
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
                _git = new GitStamp(context.Git.Detected, context.Git.Branch, context.Git.State, git?.Entries.Count ?? 0, DateTime.UtcNow);
                _disk = new DiskStamp(manifest);
                _baseline = new BaselineStamp(manifest, "captured at refresh");
                _inventory = gitStatus +
                    "\r\n\r\nDiscovered semantic folders (association not inferred):\r\n" + string.Join("\r\n", context.SemanticModelFolders) +
                    "\r\n\r\nDiscovered report folders (association not inferred):\r\n" + string.Join("\r\n", context.ReportFolders);
                var selected = context.PbipFile;
                _projects.Items.Add("Select PBIP explicitly…");
                _projects.Items.AddRange(context.PbipCandidates);
                _projects.SelectedIndex = selected == null ? 0 : _projects.Items.IndexOf(selected);
                UpdateIdentity();
                ShowInventory();
                var registryNote = TouchRegistry();
                StartWatcher(context.ProjectRoot, generation);
                _status.Text = (context.DiscoveryIncomplete ? "Incomplete inventory · " : "Inventory captured · ") + string.Join(" ", context.DiscoveryDiagnostics) +
                    (registryNote.Length > 0 ? " · " + registryNote : "");
            }
            catch (OperationCanceledException) { }
            catch (Exception) { if (!IsDisposed) _status.Text = "Project could not be read. Check path and access."; }
            finally {
                cts.Dispose(); _scanCts = null;
                if (!IsDisposed) { _commands.Enabled = _source.Enabled = _scan.Enabled = true; _vsCode.Enabled = _review.Enabled = _context != null; }
            }
        }

        private void UpdateIdentity()
        {
            if (_context == null) return;
            try
            {
                Identity = ProjectIdentity.Create(_context.ProjectRoot, _context.PbipFile);
                _identityText = "Project identity: " + Identity.Id + " · " + Identity.DisplayName + " · root " + Identity.NormalizedRoot +
                    (Identity.PbipContentHash != null ? " · pbip sha256 " + Identity.PbipContentHash.Substring(0, 12) : " · no PBIP selected");
            }
            catch (Exception) { Identity = null; _identityText = "Project identity unavailable for this selection."; }
        }

        private string TouchRegistry()
        {
            if (Identity == null) return "";
            try
            {
                var registry = ProjectRegistry.Load(RegistryPath);
                registry.Touch(Identity, DateTime.UtcNow);
                registry.Save();
                return "recent list updated (per-user, paths only)";
            }
            catch (Exception) { return "recent list not updated"; }
        }

        private void ShowRecent(Control anchor)
        {
            var menu = new ContextMenuStrip();
            try
            {
                foreach (var entry in ProjectRegistry.Load(RegistryPath).Entries.Take(15))
                {
                    var target = entry.Pbip != null ? Path.Combine(entry.Root, entry.Pbip.Replace('/', Path.DirectorySeparatorChar)) : entry.Root;
                    var item = new ToolStripMenuItem(entry.Name + " — " + Display(entry.Root));
                    item.Click += (s, e) => { _source.Text = target; RefreshProject(); };
                    menu.Items.Add(item);
                }
            }
            catch (Exception) { }
            if (menu.Items.Count == 0) menu.Items.Add(new ToolStripMenuItem("No recent projects") { Enabled = false });
            menu.Closed += (s, e) => BeginInvoke(new Action(menu.Dispose));
            menu.Show(anchor, new Point(0, anchor.Height));
        }

        private void StartWatcher(string root, int generation)
        {
            try
            {
                _watcher = new PbiBenchProjectWatcher(root, WatchDebounce);
                _watcher.Settled += signal => {
                    try { if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(() => OnWatchSignal(signal, generation))); }
                    catch (InvalidOperationException) { }
                };
                _watcher.Start();
            }
            catch (Exception) { StopWatcher(); _status.Text += " · change watcher unavailable; use Refresh."; }
        }

        private void StopWatcher() { _watcher?.Dispose(); _watcher = null; }

        private void OnWatchSignal(ProjectWatchSignal signal, int generation)
        {
            if (IsDisposed || generation != _generation || _context == null) return;
            WatchSignals++;
            if (signal.RootMissing)
            {
                // The watcher stopped itself: say so instead of going quiet.
                StopWatcher();
                _bannerText.Text = "The project folder was deleted or moved (" + _context.ProjectRoot + "). Change watching stopped; choose the project again or press Refresh.";
                _banner.Visible = true;
                _status.Text = "Project folder missing; change watcher stopped.";
                return;
            }
            var _ = RescanAsync(generation, signal.Overflow);
        }

        /// <summary>Rescans the manifest and diffs against the baseline (also callable by the smoke).</summary>
        internal async Task RescanAsync(int? generation = null, bool overflow = false)
        {
            var gen = generation ?? _generation;
            if (_context == null || _baseline == null) return;
            if (_rescanning) { _rescanPending = true; return; }
            _rescanning = true;
            try
            {
                do
                {
                    _rescanPending = false;
                    var root = _context.ProjectRoot;
                    var manifest = await Task.Run(() => FileManifest.Capture(root));
                    if (IsDisposed || gen != _generation || _baseline == null) return;
                    _disk = new DiskStamp(manifest);
                    LastChanges = StateComparison.DiskSinceBaseline(_baseline, _disk);
                    UpdateBanner(overflow);
                    ShowInventory();
                } while (_rescanPending);
            }
            catch (Exception) { if (!IsDisposed) _status.Text = "Rescan failed; use Refresh."; }
            finally { _rescanning = false; }
        }

        private void UpdateBanner(bool overflow)
        {
            var changes = LastChanges;
            if (changes == null || !changes.HasChanges) { _banner.Visible = false; return; }
            _bannerText.Text = "External changes since baseline: " + changes.Count(ExternalChangeKind.Modified) + " modified, " +
                changes.Count(ExternalChangeKind.Added) + " added, " + changes.Count(ExternalChangeKind.Removed) + " removed, " +
                changes.Count(ExternalChangeKind.Renamed) + " renamed" + (overflow ? " (watcher overflow: full rescan done)" : "") +
                ". The TE2 model was not reloaded.";
            _banner.Visible = true;
        }

        private IEnumerable<string> StampLines()
        {
            yield return LoadedState?.Invoke()?.Describe() ?? "Loaded: " + Display(_loadedIdentity) + " (source not supplied)";
            yield return _live ? "Live: connected TE2 editing context; not compared with disk" : "Live: not connected";
            if (_disk != null) yield return _disk.Describe();
            if (_baseline != null) yield return _baseline.Describe();
            if (_git != null) yield return _git.Describe();
            yield return "Disk/Loaded/Live/Git/Baseline are distinct identities and are never equated.";
        }

        private void ReviewChanges()
        {
            if (_context == null || _baseline == null) return;
            using (var dialog = new PbiBenchSyncReviewForm("Project changes", new[] { _identityText }.Concat(StampLines()), LastChanges ?? new ExternalChangeSet(new ExternalChange[0], false),
                LastChanges == null || !LastChanges.HasChanges ? "No external changes detected since the baseline." : "Files changed on disk outside this window since the baseline.", true))
            {
                if (dialog.ShowDialog(this) != DialogResult.Yes || _disk == null) return;
                _baseline = new BaselineStamp(_disk.Manifest, "accepted by user");
                LastChanges = null; UpdateBanner(false); ShowInventory();
                _status.Text = "Current disk state accepted as the baseline.";
            }
        }

        private void ReviewSaveTarget()
        {
            if (CheckSaveTarget == null) return;
            SaveTargetCheck check;
            try { check = CheckSaveTarget(); }
            catch (Exception) { _status.Text = "Save target could not be read."; return; }
            var lines = new List<string> { "Save target: " + (check.Target.Length > 0 ? check.Target : "(none)") };
            lines.AddRange(StampLines().Take(2));
            if (check.Baseline != null) lines.Add(check.Baseline.Describe());
            if (check.Disk != null) lines.Add(check.Disk.Describe());
            using (var dialog = new PbiBenchSyncReviewForm("Save target review", lines, check.Changes, check.Message,
                check.State == SaveTargetCheckState.ExternalChanges || check.State == SaveTargetCheckState.NoBaseline))
            {
                if (dialog.ShowDialog(this) == DialogResult.Yes && AcceptSaveTargetBaseline != null)
                { AcceptSaveTargetBaseline(); _status.Text = "Save target baseline accepted. This does not reload or save the model."; }
            }
        }

        private void ShowInventory()
        {
            if (_context == null) return;
            _details.Text = "Selected PBIP: " + (_context.PbipFile ?? "None") + "\r\n" + _identityText + "\r\n" + string.Join("\r\n", StampLines()) +
                "\r\nDisk inventory root: " + _context.ProjectRoot + "\r\n\r\n" + _inventory;
        }

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

        protected override void Dispose(bool disposing)
        {
            if (disposing) StopWatcher();
            base.Dispose(disposing);
        }
    }
}
