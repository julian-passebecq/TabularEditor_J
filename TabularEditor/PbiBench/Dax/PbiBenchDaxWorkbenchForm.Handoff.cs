using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PbiBench.Core.Dax;

namespace TabularEditor.PbiBench.Dax
{
    /// <summary>
    /// DAX-014 DAX Studio handoff. Only the server label and database label of the live TE2 connection are
    /// passed (as /server= and /database=, never a connection string). The query itself is placed on the clipboard for
    /// pasting. Without DAX Studio or without a live connection nothing is started: the tab is saved as .dax
    /// (existing Save / Save As flow) and the reason plus file path are reported.
    /// </summary>
    internal sealed partial class PbiBenchDaxWorkbenchForm
    {
        internal Func<DaxStudioTarget> DaxStudioTargetProvider;
        internal Func<string> DaxStudioLocator;
        internal Func<ProcessStartInfo, string> StartDaxStudio = PbiBenchDaxStudioLauncher.Start;
        internal Action<string> CopyQueryToClipboard = text => Clipboard.SetText(text, TextDataFormat.UnicodeText);
        internal string DaxStudioSettingsFolder;
        private ToolStripMenuItem _handoff;

        private void InitializeHandoff()
        {
            DaxStudioLocator = () => PbiBenchDaxStudioLauncher.Locate(DaxStudioSettingsFolder);
            _handoff = DocumentCommand("Open in DAX &Studio (handoff)…", Keys.Control | Keys.Shift | Keys.D, () => HandOffToDaxStudio());
            _handoff.ToolTipText = "Starts DAX Studio with the server and database labels only (no connection string) and copies the query to the clipboard. Otherwise saves the query as .dax.";
            var locate = new ToolStripMenuItem("DAX Studio location…");
            locate.Click += (s, e) => ChooseDaxStudioLocation();
            _fileMenu.DropDownItems.Add(new ToolStripSeparator());
            _fileMenu.DropDownItems.Add(_handoff);
            _fileMenu.DropDownItems.Add(locate);
        }

        /// <summary>Server/database labels of the live TE2 connection behind <see cref="AuthoringModel"/>, or null in draft mode.</summary>
        private DaxStudioTarget CurrentDaxStudioTarget()
        {
            if (DaxStudioTargetProvider != null) return DaxStudioTargetProvider();
            try
            {
                var handler = FormMain.Singleton?.UI?.Handler;
                var model = AuthoringModel?.Invoke();
                if (handler?.Model == null || model == null || !ReferenceEquals(handler.Model, model) || !handler.IsConnected) return null;
                var database = handler.Database;
                var server = database?.Server;
                if (server == null || !server.Connected || string.IsNullOrWhiteSpace(server.Name) || string.IsNullOrWhiteSpace(database.Name)) return null;
                return new DaxStudioTarget(server.Name, database.Name);
            }
            catch (Exception) { return null; }
        }

        /// <summary>Runs the handoff for the active tab and returns the plan that was followed (null when blocked by a running query).</summary>
        internal DaxStudioHandoffPlan HandOffToDaxStudio()
        {
            if (_runCts != null || _closing) return null;
            var query = _queryEditor.SelectionLength > 0 ? _queryEditor.SelectedText : _queryEditor.Text;
            string executable;
            try { executable = DaxStudioLocator?.Invoke(); }
            catch (Exception) { executable = null; }
            var plan = DaxStudioHandoff.Plan(executable, CurrentDaxStudioTarget());
            if (plan.Kind == DaxStudioHandoffKind.LaunchConnected)
            {
                var copied = false;
                if (!string.IsNullOrWhiteSpace(query))
                {
                    try { CopyQueryToClipboard?.Invoke(query); copied = CopyQueryToClipboard != null; }
                    catch (ExternalException) { }
                    catch (InvalidOperationException) { }
                }
                var failure = StartDaxStudio(PbiBenchDaxStudioLauncher.BuildStartInfo(plan));
                if (failure == null)
                {
                    SetStatus(plan.Reason + (copied ? " The query was copied to the clipboard; paste it into DAX Studio." :
                        string.IsNullOrWhiteSpace(query) ? "" : " The clipboard was unavailable; copy the query manually."));
                    return plan;
                }
                return SaveAndReport(failure + " Nothing else was retried.", plan);
            }
            return SaveAndReport(plan.Reason, plan);
        }

        private DaxStudioHandoffPlan SaveAndReport(string reason, DaxStudioHandoffPlan plan)
        {
            var needsSave = _document.FilePath == null || _document.IsDirty;
            if (needsSave && !SaveDocument(false))
            {
                SetStatus("DAX Studio handoff: " + reason + " The query was not saved (cancelled or failed); it is still open here.");
                return plan;
            }
            var message = reason + "\r\n\r\nQuery saved as: " + _document.FilePath;
            SetStatus("DAX Studio handoff: " + reason + " Saved: " + _document.FilePath);
            WorkspaceDialogs.Report(this, "DAX Studio handoff", message);
            return plan;
        }

        private void ChooseDaxStudioLocation()
        {
            var path = WorkspaceDialogs.ChooseDaxStudioExecutable(this);
            if (path == null) return;
            try { SetStatus("DAX Studio location saved: " + PbiBenchDaxStudioLauncher.WriteConfiguredPath(path, DaxStudioSettingsFolder)); }
            catch (Exception ex) when (ex is ArgumentException || ex is System.IO.IOException || ex is UnauthorizedAccessException)
            { SetStatus("DAX Studio location not saved: " + ex.Message); }
        }
    }
}
