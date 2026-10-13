using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using TabularEditor.Dax;
using TabularEditor.UIServices;

namespace TabularEditor.PbiBench.Dax
{
    internal sealed partial class PbiBenchDaxWorkbenchForm
    {
        private bool _formatting;
        private bool _formatDiscarded;
        private long _textRevision;
        private ToolStripMenuItem _formatLong, _formatShort;

        private void InitializeFormatting()
        {
            _queryEditor.TextChanged += (s, e) => _textRevision++;
            var menu = new ToolStripMenuItem("Remote formatting");
            var consent = new ToolStripMenuItem("Allow sending DAX to SQLBI for this session")
            {
                ToolTipText = "Sends DAX, formatting options and app/version fields. Applies to inherited expression, script and CLI formatting. No model telemetry. Restart disables consent."
            };
            consent.Click += (s, e) =>
            {
                RemoteFormatterConsent.SetEnabled(!RemoteFormatterConsent.Enabled);
                consent.Checked = RemoteFormatterConsent.Enabled;
                SetStatus(consent.Checked
                    ? "SQLBI remote formatting enabled for this session, including inherited calls. Sends DAX, options and app/version; no model telemetry."
                    : "Remote formatting disabled. In-flight requests may already have sent DAX; their results will be discarded.");
            };
            menu.DropDownOpening += (s, e) => consent.Checked = RemoteFormatterConsent.Enabled;
            _formatLong = DocumentCommand("Format entire draft via SQLBI", Keys.F6, () => FormatDraft(false));
            _formatShort = DocumentCommand("Short format entire draft via SQLBI", Keys.Control | Keys.F6, () => FormatDraft(true));
            menu.DropDownItems.AddRange(new ToolStripItem[] { consent, _formatLong, _formatShort });
            MainMenuStrip.Items.Add(menu);
        }

        private void CancelFormatting() { _formatDiscarded = true; SetStatus("Formatting response cancelled; waiting for the pending remote request to finish."); }

        private async void FormatDraft(bool shortFormat)
        {
            if (_runCts != null || _formatting || _closing) return;
            try { RemoteFormatterConsent.RequireEnabled(); }
            catch (InvalidOperationException ex) { SetStatus(ex.Message); return; }
            var revision = _textRevision;
            var consent = RemoteFormatterConsent.Revision;
            var text = _queryEditor.Text;
            var separators = Preferences.Current.UseSemicolonsAsSeparators;
            var skipSpace = Preferences.Current.DaxFormatterSkipSpaceAfterFunctionName;
            _formatting = true;
            _formatDiscarded = false;
            _cancelButton.Enabled = true;
            _executeButton.Enabled = _formatLong.Enabled = _formatShort.Enabled = false;
            SetStatus("Formatting entire draft remotely via SQLBI…");
            try
            {
                var result = await Task.Run(() => DaxFormatterProxy.Instance.FormatDax(text, separators, shortFormat, skipSpace));
                if (_closing || IsDisposed || Disposing) return;
                if (_formatDiscarded || revision != _textRevision || consent != RemoteFormatterConsent.Revision)
                { SetStatus("Formatting response discarded because it was cancelled or the draft/consent changed."); return; }
                if (result == null || string.IsNullOrWhiteSpace(result.FormattedDax) || result.errors?.Count > 0)
                { SetStatus("Remote formatting returned no valid result; draft unchanged."); return; }
                _queryEditor.SelectAll();
                _queryEditor.SelectedText = result.FormattedDax;
                SetStatus("Entire draft formatted via SQLBI · Ctrl+Z to undo.");
            }
            catch (Exception) { if (!_closing && !IsDisposed && !Disposing) SetStatus("Remote formatting failed; draft unchanged."); }
            finally
            {
                _formatting = false;
                if (!_closing && !IsDisposed && !Disposing)
                {
                    _cancelButton.Enabled = false;
                    _executeButton.Enabled = _executor != null;
                    _formatLong.Enabled = _formatShort.Enabled = true;
                }
            }
        }
    }
}
