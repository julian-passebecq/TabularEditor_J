using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using PbiBench.Core.Interop;
using PbiBench.Core.Snapshot;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Mcp
{
    /// <summary>In-memory typed proposal. Only a host UI approval calls Apply; there is no wire grant.</summary>
    internal sealed class PbiBenchLiveBridgeProposal
    {
        private readonly TabularModelHandler _handler;
        private readonly Measure _measure;
        private readonly Func<bool> _current;
        internal readonly Dictionary<string, string> Before;
        internal readonly Dictionary<string, string> After;
        internal bool Applied { get; private set; }
        internal string Target => _handler.Model.Name + " / " + _measure.Table.Name + " / " + _measure.Name;
        internal PbiBenchLiveBridgeProposal(TabularModelHandler handler, Measure measure, IDictionary<string, string> changes, Func<bool> current)
        {
            _handler = handler; _measure = measure; _current = current;
            Before = changes.Keys.ToDictionary(k => k, k => Read(measure, k), StringComparer.Ordinal);
            After = new Dictionary<string, string>(changes, StringComparer.Ordinal);
            if (After.Keys.Any(k => k != "description" && k != "expression" && k != "folder") || After.Count == 0 || After.Values.Sum(x => (long)x.Length) > Te2LiveBridgeV1.MaxValueChars || After.Values.Any(ModelSnapshotSerializer.LooksLikeCredential)) throw new InvalidOperationException("INVALID_TYPED_CHANGE");
        }
        private static string Read(Measure m, string property)
        {
            switch (property) { case "description": return m.Description ?? ""; case "expression": return m.Expression ?? ""; case "folder": return m.DisplayFolder ?? ""; default: throw new InvalidOperationException("PROPERTY"); }
        }
        private static void Write(Measure m, string property, string value)
        {
            switch (property) { case "description": m.Description = value; break; case "expression": m.Expression = value; break; case "folder": m.DisplayFolder = value; break; default: throw new InvalidOperationException("PROPERTY"); }
        }
        internal void Apply()
        {
            if (Applied || !_current() || _measure.IsRemoved || !_handler.UndoManager.Enabled || _handler.UndoManager.BatchDepth != 0 || Before.Any(p => Read(_measure, p.Key) != p.Value)) throw new InvalidOperationException("STALE_OR_BUSY");
            _handler.BeginUpdate("TE2 Galaxy reviewed proposal");
            var committed = false;
            try
            {
                // No callbacks/await between freshness admission and these typed TOM setters.
                foreach (var pair in After) { Write(_measure, pair.Key, pair.Value); if (Read(_measure, pair.Key) != pair.Value) throw new InvalidOperationException("READBACK"); }
                committed = true;
            }
            finally { _handler.EndUpdate(rollback: !committed); }
            Applied = true;
        }
    }

    internal sealed class PbiBenchLiveBridgeReviewForm : Form
    {
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 100 };
        internal PbiBenchLiveBridgeReviewForm(PbiBenchLiveBridgeProposal proposal, CancellationToken token)
        {
            Text = "TE2 Galaxy · review loaded-model changes"; Size = new Size(880, 560); MinimumSize = new Size(600, 400);
            StartPosition = FormStartPosition.CenterParent; AutoScaleMode = AutoScaleMode.Dpi;
            var diff = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, AccessibleName = "Exact proposed changes" };
            diff.Text = string.Join(Environment.NewLine + Environment.NewLine, proposal.After.Select(p => p.Key + Environment.NewLine + "BEFORE:" + Environment.NewLine + proposal.Before[p.Key] + Environment.NewLine + "AFTER:" + Environment.NewLine + p.Value));
            var target = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Text = proposal.Target, AccessibleName = "Exact loaded model, table and measure target" };
            var status = new Label { Dock = DockStyle.Top, Height = 52, Text = "Loaded model only. Review exact changes. Approve applies one TE2 Undo batch; no Save, query or deployment. Channel loss after admission can leave an unknown outcome: inspect TE2 before retrying." };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            var approve = new Button { Text = "Approve reviewed changes", AutoSize = true, Name = "bridgeApprove" };
            var reject = new Button { Text = "Reject", DialogResult = DialogResult.Cancel, AutoSize = true };
            approve.Click += (s, e) =>
            {
                approve.Enabled = false;
                try { token.ThrowIfCancellationRequested(); proposal.Apply(); DialogResult = DialogResult.OK; Close(); }
                catch (Exception) { status.Text = "Not applied: proposal stale, channel closed or setter rejected. Review a fresh proposal; no implicit retry."; }
            };
            actions.Controls.Add(approve); actions.Controls.Add(reject); Controls.Add(diff); Controls.Add(target); Controls.Add(status); Controls.Add(actions); CancelButton = reject;
            // Enter is deliberately not an approval shortcut.
            _timer.Tick += (s, e) => { if (token.IsCancellationRequested) { DialogResult = DialogResult.Cancel; Close(); } };
            Shown += (s, e) => _timer.Start();
        }
        protected override void Dispose(bool disposing) { if (disposing) _timer.Dispose(); base.Dispose(disposing); }
    }

    internal sealed class PbiBenchLiveBridgeControlForm : Form
    {
        private PbiBenchLiveBridgeHost _host;
        private readonly Func<bool, PbiBenchLiveBridgeHost> _start;
        internal PbiBenchLiveBridgeControlForm(Func<bool, PbiBenchLiveBridgeHost> start)
        {
            _start = start; Text = "TE2 Galaxy live bridge · selected instance"; Size = new Size(820, 290); StartPosition = FormStartPosition.CenterParent;
            var label = new Label { Dock = DockStyle.Top, Height = 55, Text = "Explicit local owner-only IPC for this TE2 instance. No automatic discovery, network, row reads or agent configuration. Copy this descriptor into the stdio sidecar --live option. Closing this window stops the endpoint." };
            var descriptor = new TextBox { Dock = DockStyle.Top, ReadOnly = true, Multiline = true, Height = 65, AccessibleName = "Selected instance descriptor" };
            var expressions = new CheckBox { Text = "Allow measure expressions in live reads (off by default)", AutoSize = true };
            var startButton = new Button { Text = "Start bridge", AutoSize = true };
            var stop = new Button { Text = "Stop bridge", AutoSize = true };
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            startButton.Click += (s, e) => { if (_host != null) return; try { _host = _start(expressions.Checked); descriptor.Text = _host.Descriptor; expressions.Enabled = false; } catch (Exception) { descriptor.Text = "Bridge unavailable; no security fallback."; } };
            stop.Click += (s, e) => { _host?.Dispose(); _host = null; descriptor.Clear(); expressions.Enabled = true; };
            row.Controls.Add(expressions); row.Controls.Add(startButton); row.Controls.Add(stop); Controls.Add(descriptor); Controls.Add(label); Controls.Add(row);
        }
        protected override void Dispose(bool disposing) { if (disposing) { _host?.Dispose(); _host = null; } base.Dispose(disposing); }
    }
}
