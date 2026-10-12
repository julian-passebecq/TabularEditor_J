using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using PbiBench.Core.Snapshot;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Mcp
{
    /// <summary>
    /// Projects the loaded model into the neutral read-only <see cref="ModelSnapshotV1"/> for the
    /// separate MCP companion. Publication is always an explicit user action; nothing is pushed
    /// automatically and no connection details are included.
    /// </summary>
    internal static class PbiBenchModelSnapshotPublisher
    {
        public static ModelSnapshotV1 Build(TabularEditor.TOMWrapper.Model model, IEnumerable<ITabularNamedObject> selection, bool includeExpressions, DateTime utcNow)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var snapshot = new ModelSnapshotV1
            {
                ModelName = model.Name ?? "",
                CompatibilityLevel = model.Database?.CompatibilityLevel ?? 0,
                IncludesExpressions = includeExpressions
            };
            foreach (var table in model.Tables.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                var t = new SnapshotTable { Name = table.Name, IsHidden = table.IsHidden, Description = table.Description ?? "" };
                foreach (var column in table.Columns.OrderBy(c => c.Name, StringComparer.Ordinal))
                    t.Columns.Add(new SnapshotColumn { Name = column.Name, DataType = column.DataType.ToString(), IsHidden = column.IsHidden, DisplayFolder = column.DisplayFolder ?? "" });
                foreach (var measure in table.Measures.OrderBy(m => m.Name, StringComparer.Ordinal))
                    t.Measures.Add(new SnapshotMeasure
                    {
                        Name = measure.Name, DisplayFolder = measure.DisplayFolder ?? "", FormatString = measure.FormatString ?? "",
                        IsHidden = measure.IsHidden, Description = measure.Description ?? "",
                        Expression = includeExpressions ? measure.Expression ?? "" : null
                    });
                snapshot.Tables.Add(t);
            }
            foreach (var r in model.Relationships.OfType<SingleColumnRelationship>())
            {
                // Unresolved endpoints stay visible instead of being dropped.
                snapshot.Relationships.Add(new SnapshotRelationship
                {
                    FromTable = r.FromColumn?.Table?.Name ?? "(unresolved)", FromColumn = r.FromColumn?.Name ?? "(unresolved)",
                    ToTable = r.ToColumn?.Table?.Name ?? "(unresolved)", ToColumn = r.ToColumn?.Name ?? "(unresolved)",
                    IsActive = r.IsActive, CrossFilter = r.CrossFilteringBehavior.ToString()
                });
            }
            foreach (var obj in (selection ?? Enumerable.Empty<ITabularNamedObject>()).Take(200))
                snapshot.Selection.Add(new SnapshotSelection
                {
                    Kind = obj.ObjectType.ToString(), Name = obj.Name ?? "",
                    Table = (obj as ITabularTableObject)?.Table?.Name ?? ""
                });

            // Content revision: changes exactly when the published metadata changes.
            snapshot.ModelRevision = Revision(ModelSnapshotSerializer.Serialize(snapshot));
            snapshot.PublishedAtUtc = utcNow.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            return snapshot;
        }

        public static string DefaultPath() =>
            ModelSnapshotSerializer.DefaultPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

        private static string Revision(string json)
        {
            using (var sha = SHA256.Create())
                return "sha256:" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "").Substring(0, 16).ToLowerInvariant();
        }
    }

    /// <summary>Explicit publication dialog: shows the destination and the expression opt-in.</summary>
    internal sealed class PbiBenchSnapshotPublishForm : Form
    {
        private readonly Func<TabularEditor.TOMWrapper.Model> _model;
        private readonly Func<IEnumerable<ITabularNamedObject>> _selection;
        private readonly CheckBox _expressions = new CheckBox { Text = "Include measure DAX expressions (off by default)", AutoSize = true };
        private readonly Label _status = new Label { AutoSize = true, MaximumSize = new Size(560, 0) };

        public PbiBenchSnapshotPublishForm(Func<TabularEditor.TOMWrapper.Model> model, Func<IEnumerable<ITabularNamedObject>> selection, string path = null)
        {
            _model = model; _selection = selection;
            Path = path ?? PbiBenchModelSnapshotPublisher.DefaultPath();
            Text = "Publish read-only snapshot for MCP";
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(12);
            var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            layout.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(560, 0),
                Text = "Writes model metadata (tables, columns, measures, relationships, current selection) for the separate " +
                       "TE2 Enhanced MCP companion. No connection details, credentials or data rows. The file is a point-in-time " +
                       "copy: publish again after changes.\n\nDestination: " + Path
            });
            layout.Controls.Add(_expressions);
            var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
            var publish = new Button { Text = "&Publish", AutoSize = true };
            var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
            publish.Click += (s, e) => Publish();
            buttons.Controls.Add(publish); buttons.Controls.Add(close);
            layout.Controls.Add(buttons);
            layout.Controls.Add(_status);
            Controls.Add(layout);
            AcceptButton = publish; CancelButton = close;
        }

        public string Path { get; }
        public bool IncludeExpressions { get => _expressions.Checked; set => _expressions.Checked = value; }
        public ModelSnapshotV1 LastPublished { get; private set; }

        public bool Publish()
        {
            var model = _model();
            if (model == null) { _status.Text = "No model loaded."; return false; }
            try
            {
                var snapshot = PbiBenchModelSnapshotPublisher.Build(model, _selection?.Invoke(), IncludeExpressions, DateTime.UtcNow);
                ModelSnapshotSerializer.WriteFile(Path, snapshot);
                LastPublished = snapshot;
                _status.Text = $"Published {snapshot.Tables.Count} tables, {snapshot.Tables.Sum(t => t.Measures.Count)} measures at {snapshot.PublishedAtUtc} ({snapshot.ModelRevision}).";
                return true;
            }
            catch (Exception error)
            {
                // Refusals (for example credential-looking text) are reported, never partially written.
                _status.Text = "Not published: " + error.Message;
                return false;
            }
        }
    }
}
