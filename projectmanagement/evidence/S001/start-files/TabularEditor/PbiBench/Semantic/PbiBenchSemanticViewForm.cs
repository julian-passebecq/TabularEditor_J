using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using PbiBench.Core.Navigation;
using PbiBench.Core.Semantic;

namespace TabularEditor.PbiBench.Semantic
{
    /// <summary>
    /// Read-only dependency explorer over a PbiBench semantic snapshot. The form never mutates
    /// TOM objects; it only returns an object id to UIController, which delegates navigation to
    /// TE2's native Goto implementation.
    /// </summary>
    internal sealed class PbiBenchSemanticViewForm : Form
    {
        private readonly PbiBenchSemanticSnapshot _snapshot;
        private readonly TextBox _queryBox;
        private readonly ListView _objects;
        private readonly ListView _dependencies;
        private readonly ListView _dependants;
        private readonly TabControl _tabs;
        private readonly CheckBox _includeIndirect;
        private readonly Label _status;

        public PbiBenchSemanticViewForm(PbiBenchSemanticSnapshot snapshot)
        {
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

            Text = "PbiBench Semantic View";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 520);
            Size = new Size(1180, 700);
            ShowInTaskbar = false;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.Dpi;

            var top = new Panel { Dock = DockStyle.Top, Height = 54, Padding = new Padding(10, 10, 10, 6) };
            _queryBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Font = new Font(SystemFonts.MessageBoxFont.FontFamily, 10.5f),
                AccessibleName = "Semantic View object search"
            };
            _queryBox.TextChanged += (s, e) => RefreshObjects();
            top.Controls.Add(_queryBox);

            var split = new SplitContainer
            {
                // Docking cannot size an unparented control before its panel minima are applied.
                Size = new Size(ClientSize.Width, ClientSize.Height - top.Height),
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 390,
                Panel1MinSize = 300,
                Panel2MinSize = 450
            };

            _objects = CreateObjectList();
            _objects.SelectedIndexChanged += (s, e) => RefreshRelationships();
            _objects.DoubleClick += (s, e) => AcceptNavigation(GetSelectedId(_objects));
            split.Panel1.Controls.Add(_objects);

            var right = new Panel { Dock = DockStyle.Fill };
            var options = new Panel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 6, 8, 2) };
            _includeIndirect = new CheckBox
            {
                AutoSize = true,
                Text = "Include indirect dependencies",
                Checked = false
            };
            _includeIndirect.CheckedChanged += (s, e) => RefreshRelationships();
            options.Controls.Add(_includeIndirect);

            _tabs = new TabControl { Dock = DockStyle.Fill };
            _dependencies = CreateRelationshipList();
            _dependants = CreateRelationshipList();
            _dependencies.DoubleClick += (s, e) => AcceptNavigation(GetSelectedId(_dependencies));
            _dependants.DoubleClick += (s, e) => AcceptNavigation(GetSelectedId(_dependants));

            var dependsTab = new TabPage("Depends on");
            dependsTab.Controls.Add(_dependencies);
            var usedByTab = new TabPage("Referenced by");
            usedByTab.Controls.Add(_dependants);
            _tabs.TabPages.Add(dependsTab);
            _tabs.TabPages.Add(usedByTab);
            right.Controls.Add(_tabs);
            right.Controls.Add(options);
            split.Panel2.Controls.Add(right);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(10, 5, 10, 5) };
            var goTo = new Button { Text = "Go to in model", Dock = DockStyle.Right, Width = 120 };
            goTo.Click += (s, e) => AcceptNavigation(GetPreferredNavigationId());
            _status = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            footer.Controls.Add(goTo);
            footer.Controls.Add(_status);

            Controls.Add(split);
            Controls.Add(footer);
            Controls.Add(top);

            RefreshObjects();
        }

        public string SelectedId { get; private set; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _queryBox.Focus();
            _queryBox.SelectAll();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }

            if (keyData == (Keys.Control | Keys.L))
            {
                _queryBox.Focus();
                _queryBox.SelectAll();
                return true;
            }

            if (keyData == Keys.Enter)
            {
                var id = GetPreferredNavigationId();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    AcceptNavigation(id);
                    return true;
                }
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static ListView CreateObjectList()
        {
            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                ShowItemToolTips = true
            };
            list.Columns.Add("Object", 185);
            list.Columns.Add("Kind", 90);
            list.Columns.Add("Location", 360);
            return list;
        }

        private static ListView CreateRelationshipList()
        {
            var list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                ShowItemToolTips = true
            };
            list.Columns.Add("Object", 180);
            list.Columns.Add("Kind", 90);
            list.Columns.Add("Depth", 55);
            list.Columns.Add("Refs", 45);
            list.Columns.Add("DAX property", 150);
            list.Columns.Add("Location", 330);
            return list;
        }

        private void RefreshObjects()
        {
            var matches = QuickOpenMatcher.Search(_snapshot.Index.Descriptors, _queryBox.Text, 250);
            var previousId = GetSelectedId(_objects);

            _objects.BeginUpdate();
            try
            {
                _objects.Items.Clear();
                foreach (var match in matches)
                {
                    var descriptor = match.Object;
                    var label = descriptor.Name + (descriptor.IsHidden ? "  (hidden)" : string.Empty);
                    var item = new ListViewItem(label)
                    {
                        Tag = descriptor.Id,
                        ToolTipText = descriptor.EffectivePath
                    };
                    item.SubItems.Add(GetKindLabel(descriptor.Kind));
                    item.SubItems.Add(descriptor.EffectivePath);
                    _objects.Items.Add(item);
                }

                SelectById(_objects, previousId);
                if (_objects.SelectedItems.Count == 0 && _objects.Items.Count > 0)
                {
                    _objects.Items[0].Selected = true;
                    _objects.Items[0].Focused = true;
                }
            }
            finally
            {
                _objects.EndUpdate();
            }

            RefreshRelationships();
        }

        private void RefreshRelationships()
        {
            var id = GetSelectedId(_objects);
            if (string.IsNullOrWhiteSpace(id))
            {
                _dependencies.Items.Clear();
                _dependants.Items.Clear();
                _status.Text = "No semantic object selected.";
                return;
            }

            var dependencyCount = PopulateRelationships(_dependencies, id, true, _includeIndirect.Checked);
            var dependantCount = PopulateRelationships(_dependants, id, false, _includeIndirect.Checked);
            var descriptor = _snapshot.GetDescriptor(id);
            _status.Text = (descriptor?.EffectivePath ?? id)
                + "  ·  depends on " + dependencyCount
                + "  ·  referenced by " + dependantCount
                + (_includeIndirect.Checked ? "  ·  transitive view" : "  ·  direct view")
                + "  ·  Ctrl+L search";
        }

        private int PopulateRelationships(ListView list, string startId, bool forward, bool includeIndirect)
        {
            var rows = new List<RelationshipRow>();
            if (includeIndirect)
            {
                var nodes = forward
                    ? _snapshot.Graph.GetDeepDependencies(startId)
                    : _snapshot.Graph.GetDeepDependants(startId);

                foreach (var node in nodes)
                {
                    var directEdge = node.Depth == 1
                        ? FindDirectEdge(startId, node.ObjectId, forward)
                        : null;
                    rows.Add(CreateRow(node.ObjectId, node.Depth, directEdge));
                }
            }
            else
            {
                var edges = forward
                    ? _snapshot.Graph.GetDependencies(startId)
                    : _snapshot.Graph.GetDependants(startId);

                foreach (var edge in edges)
                {
                    var otherId = forward ? edge.TargetId : edge.SourceId;
                    rows.Add(CreateRow(otherId, 1, edge));
                }
            }

            list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var row in rows
                    .OrderBy(r => r.Depth)
                    .ThenBy(r => r.Descriptor?.EffectivePath ?? r.Id, StringComparer.OrdinalIgnoreCase))
                {
                    var descriptor = row.Descriptor;
                    var item = new ListViewItem(descriptor?.Name ?? row.Id)
                    {
                        Tag = row.Id,
                        ToolTipText = descriptor?.EffectivePath ?? row.Id
                    };
                    item.SubItems.Add(descriptor == null ? string.Empty : GetKindLabel(descriptor.Kind));
                    item.SubItems.Add(row.Depth.ToString());
                    item.SubItems.Add(row.ReferenceCount > 0 ? row.ReferenceCount.ToString() : string.Empty);
                    item.SubItems.Add(row.Properties);
                    item.SubItems.Add(descriptor?.EffectivePath ?? string.Empty);
                    list.Items.Add(item);
                }
            }
            finally
            {
                list.EndUpdate();
            }

            return rows.Count;
        }

        private SemanticDependencyEdge FindDirectEdge(string startId, string otherId, bool forward)
        {
            var edges = forward
                ? _snapshot.Graph.GetDependencies(startId)
                : _snapshot.Graph.GetDependants(startId);
            return edges.FirstOrDefault(e => string.Equals(
                forward ? e.TargetId : e.SourceId,
                otherId,
                StringComparison.Ordinal));
        }

        private RelationshipRow CreateRow(string id, int depth, SemanticDependencyEdge edge)
        {
            return new RelationshipRow
            {
                Id = id,
                Depth = depth,
                Descriptor = _snapshot.GetDescriptor(id),
                ReferenceCount = edge?.ReferenceCount ?? 0,
                Properties = edge == null ? string.Empty : string.Join(", ", edge.Properties ?? Array.Empty<string>())
            };
        }

        private string GetPreferredNavigationId()
        {
            var active = _tabs.SelectedTab != null && _tabs.SelectedTab.Text == "Referenced by"
                ? _dependants
                : _dependencies;
            var relationshipId = GetSelectedId(active);
            return !string.IsNullOrWhiteSpace(relationshipId) ? relationshipId : GetSelectedId(_objects);
        }

        private void AcceptNavigation(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            SelectedId = id;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static string GetSelectedId(ListView list)
        {
            return list != null && list.SelectedItems.Count == 1
                ? list.SelectedItems[0].Tag as string
                : null;
        }

        private static void SelectById(ListView list, string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            foreach (ListViewItem item in list.Items)
            {
                if (!string.Equals(item.Tag as string, id, StringComparison.Ordinal)) continue;
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
                return;
            }
        }

        private static string GetKindLabel(SemanticObjectKind kind)
        {
            switch (kind)
            {
                case SemanticObjectKind.CalculationGroup: return "Calc group";
                case SemanticObjectKind.CalculationItem: return "Calc item";
                case SemanticObjectKind.DataSource: return "Data source";
                default: return kind.ToString();
            }
        }

        private sealed class RelationshipRow
        {
            public string Id { get; set; }
            public int Depth { get; set; }
            public SemanticObjectDescriptor Descriptor { get; set; }
            public int ReferenceCount { get; set; }
            public string Properties { get; set; }
        }
    }
}
