using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using PbiBench.Core.Documents;

namespace TabularEditor.PbiBench.Dax
{
    /// <summary>
    /// DAX-013 opt-in recovery through the shared S4 <see cref="RecoveryStore"/> (kind "dax"). Off by default:
    /// nothing is read from or written to %LOCALAPPDATA%\TE2Enhanced\recovery until the user accepts the
    /// disclosure (marker file "recovery.dax.optin", separate from the script workspace consent). Dirty tabs are
    /// copied 3 s after the last edit; a copy is deleted when its tab is saved, replaced after "Don't save",
    /// or closed. Copies survive a crash and are offered as unsaved tabs when the Workbench opens.
    /// </summary>
    internal sealed partial class PbiBenchDaxWorkbenchForm
    {
        internal const string RecoveryKind = "dax";
        private readonly System.Windows.Forms.Timer _recoveryTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        private string _recoveryRoot = RecoveryStoreOptions.DefaultRoot;
        private bool _recoveryEnabled;
        private ToolStripMenuItem _recoveryOptInItem;

        internal IDaxWorkspaceDialogs WorkspaceDialogs { get; set; } = new PbiBenchDaxWorkspaceDialogs();
        internal bool RecoveryEnabled => _recoveryEnabled;

        /// <summary>Recovery folder (default per-user). Setting it re-reads the opt-in marker next to it.</summary>
        internal string RecoveryRoot
        {
            get => _recoveryRoot;
            set
            {
                _recoveryRoot = Path.GetFullPath(value);
                _recoveryEnabled = OptInMarkerExists();
                if (_recoveryOptInItem != null) _recoveryOptInItem.Checked = _recoveryEnabled;
            }
        }

        private string OptInMarkerPath => OptInMarkerFor(_recoveryRoot);
        private static string OptInMarkerFor(string root) => root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".dax.optin";

        private void InitializeRecovery()
        {
            _recoveryEnabled = OptInMarkerExists();
            var menu = new ToolStripMenuItem("&Recovery");
            _recoveryOptInItem = new ToolStripMenuItem("Keep local recovery copies of unsaved DAX (opt-in)…") { Checked = _recoveryEnabled };
            _recoveryOptInItem.Click += (s, e) => SetRecoveryOptIn(!_recoveryEnabled);
            var restore = new ToolStripMenuItem("Restore recovered DAX drafts…");
            restore.Click += (s, e) => { if (OfferRecovery() == 0 && _recoveryEnabled) SetStatus("No other recovered DAX drafts were found."); };
            var delete = new ToolStripMenuItem("Delete DAX recovery copies");
            delete.Click += (s, e) => DeleteRecoveryCopies();
            menu.DropDownOpening += (s, e) => { _recoveryOptInItem.Checked = _recoveryEnabled; restore.Enabled = _recoveryEnabled; };
            menu.DropDownItems.AddRange(new ToolStripItem[] { _recoveryOptInItem, restore, delete });
            _fileMenu.DropDownItems.Add(new ToolStripSeparator());
            _fileMenu.DropDownItems.Add(menu);
            _recoveryTimer.Tick += (s, e) => { _recoveryTimer.Stop(); SaveRecoveryNow(); };
            Disposed += (s, e) => _recoveryTimer.Dispose();
            // The prompt needs a visible owner, so it waits for Load; without opt-in nothing is read from disk.
            Load += (s, e) => { if (_recoveryEnabled) OfferRecovery(); };
        }

        private bool OptInMarkerExists()
        {
            try { return File.Exists(OptInMarkerPath); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException) { return false; }
        }

        private RecoveryStore CreateRecoveryStore(bool enabled) => new RecoveryStore(new RecoveryStoreOptions(_recoveryRoot, enabled));

        private void ScheduleRecovery()
        {
            if (!_recoveryEnabled || _closing || IsDisposed) return;
            _recoveryTimer.Stop();
            _recoveryTimer.Start();
        }

        /// <summary>Writes a copy of every dirty tab and removes copies of tabs that are clean again. False when off or on failure.</summary>
        internal bool SaveRecoveryNow()
        {
            if (!_recoveryEnabled) return false;
            try
            {
                var store = CreateRecoveryStore(true);
                foreach (var tab in Tabs)
                {
                    if (tab.Document.IsDirty)
                    {
                        store.Save(tab.RecoveryId, RecoveryKind, tab.Document.Title + tab.TitleSuffix, tab.Document.Text, DateTime.UtcNow);
                        tab.HasRecoveryCopy = true;
                    }
                    else if (tab.HasRecoveryCopy)
                    {
                        store.Delete(tab.RecoveryId);
                        tab.HasRecoveryCopy = false;
                    }
                }
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                SetStatus("Recovery copy not written: " + ex.Message);
                return false;
            }
        }

        /// <summary>Offers recovered DAX copies (not already open) as unsaved tabs. Returns the number restored.</summary>
        internal int OfferRecovery()
        {
            if (!_recoveryEnabled) return 0;
            RecoveryEntry[] entries;
            try
            {
                var open = Tabs.Select(t => t.RecoveryId).ToArray();
                entries = CreateRecoveryStore(true).List().Where(e => e.Kind == RecoveryKind && !open.Contains(e.DocumentId)).ToArray();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SetStatus("Recovery folder could not be read: " + ex.Message); return 0; }
            var intact = entries.Where(e => e.IsIntact).Take(Math.Max(0, _documents.MaxDocuments - _documents.Count)).ToArray();
            var damaged = entries.Length - entries.Count(e => e.IsIntact);
            if (intact.Length == 0)
            {
                if (damaged > 0) SetStatus(damaged + " DAX recovery cop" + (damaged == 1 ? "y" : "ies") + " failed the hash check and were not offered.");
                return 0;
            }
            if (!WorkspaceDialogs.OfferRestore(this, intact.Length, _recoveryRoot))
            {
                SetStatus("Recovered DAX drafts kept on this computer; File > Recovery restores or deletes them.");
                return 0;
            }
            var restored = 0;
            WorkbenchTab first = null;
            foreach (var entry in intact)
            {
                string text;
                try { text = CreateRecoveryStore(true).Read(entry.DocumentId); }
                catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is UnauthorizedAccessException) { damaged++; continue; }
                var document = new DaxDocument();
                document.New(text, unsaved: true);
                var label = entry.Title.Length > 0 ? entry.Title.Replace(" (recovered)", "") : "draft";
                var tab = AddTab(document, false, " · recovered (" + label + ")");
                if (tab == null) break;
                tab.RecoveryId = entry.DocumentId;
                tab.HasRecoveryCopy = true;
                first = first ?? tab;
                restored++;
            }
            if (first != null) ActivateTab(first);
            SetStatus("Restored " + restored + " DAX draft(s) from local recovery as unsaved tabs." +
                (damaged > 0 ? " " + damaged + " cop" + (damaged == 1 ? "y" : "ies") + " failed the hash check and stayed on disk." : ""));
            return restored;
        }

        /// <summary>Turns recovery on (after the disclosure is accepted) or off. Off keeps existing copies until deleted.</summary>
        internal bool SetRecoveryOptIn(bool enabled)
        {
            try
            {
                if (enabled)
                {
                    if (!WorkspaceDialogs.ConfirmRecoveryOptIn(this, CreateRecoveryStore(true).Disclosure)) { SetStatus("Recovery copies stay off."); return false; }
                    Directory.CreateDirectory(Path.GetDirectoryName(OptInMarkerPath));
                    if (!File.Exists(OptInMarkerPath)) File.WriteAllText(OptInMarkerPath, "TE2 Enhanced: DAX recovery copies enabled by the user.");
                    _recoveryEnabled = true;
                    SaveRecoveryNow();
                    SetStatus("Local recovery copies on for unsaved DAX tabs (" + _recoveryRoot + ").");
                }
                else
                {
                    if (File.Exists(OptInMarkerPath)) File.Delete(OptInMarkerPath);
                    _recoveryEnabled = false;
                    _recoveryTimer.Stop();
                    SetStatus("Local recovery copies off. Existing copies stay until File > Recovery > Delete.");
                }
                if (_recoveryOptInItem != null) _recoveryOptInItem.Checked = _recoveryEnabled;
                return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SetStatus("Recovery setting not changed: " + ex.Message); return false; }
        }

        /// <summary>Deletes every DAX recovery copy (script copies and other files are left alone).</summary>
        internal int DeleteRecoveryCopies()
        {
            try
            {
                var count = DeleteAllRecoveryCopies(_recoveryRoot, Tabs.Select(t => t.RecoveryId));
                foreach (var tab in Tabs) tab.HasRecoveryCopy = false;
                SetStatus("Deleted " + count + " DAX recovery cop" + (count == 1 ? "y" : "ies") + " from " + _recoveryRoot + ".");
                return count;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SetStatus("Recovery copies not deleted: " + ex.Message); return 0; }
        }

        /// <summary>Deletes kind "dax" copies (plus the given ids). Usable without an open Workbench.</summary>
        internal static int DeleteAllRecoveryCopies(string root, System.Collections.Generic.IEnumerable<string> extraIds = null)
        {
            var store = new RecoveryStore(new RecoveryStoreOptions(root ?? RecoveryStoreOptions.DefaultRoot, true));
            var ids = store.List().Where(e => e.Kind == RecoveryKind).Select(e => e.DocumentId)
                .Concat(extraIds ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).ToArray();
            return ids.Count(store.Delete);
        }

        private void DiscardRecoveryCopy(WorkbenchTab tab)
        {
            if (tab == null || (!tab.HasRecoveryCopy && !_recoveryEnabled)) return;
            try { CreateRecoveryStore(false).Delete(tab.RecoveryId); tab.HasRecoveryCopy = false; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { SetStatus("Recovery copy not deleted: " + ex.Message); }
        }

        private void OnActiveDocumentSaved()
        {
            var tab = ActiveTab;
            DiscardRecoveryCopy(tab);
            tab.RecoveryId = tab.Document.Id;
            tab.TitleSuffix = "";
            tab.External = DaxDiskState.Unchanged;
            UpdateTabTitle(tab);
            ShowExternalBanner(tab);
        }

        private void OnActiveContentReplaced()
        {
            var tab = ActiveTab;
            DiscardRecoveryCopy(tab);
            tab.RecoveryId = tab.Document.Id;
            ScheduleRecovery();
        }

        /// <summary>The user confirmed closing: every tab was saved or explicitly discarded, so its copy goes too.</summary>
        private void OnWorkspaceClosed()
        {
            _recoveryTimer.Stop();
            foreach (var tab in Tabs) DiscardRecoveryCopy(tab);
        }
    }
}
