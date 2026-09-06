using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using PbiBench.Core.Dax;

namespace TabularEditor.PbiBench.Dax
{
    /// <summary>
    /// Bounded, privacy-conscious DAX query surface. Query history lives only in memory; export
    /// occurs only after an explicit Save dialog. The form remains useful in draft mode when the
    /// loaded model does not expose a live Analysis Services connection.
    /// </summary>
    internal sealed class PbiBenchDaxWorkbenchForm : Form
    {
        private readonly IDaxQueryExecutor _executor;
        private readonly string _connectionLabel;
        private readonly string _unavailableReason;
        private readonly DaxQueryHistory _history = new DaxQueryHistory(100);

        private readonly RichTextBox _queryEditor;
        private readonly Button _executeButton;
        private readonly Button _cancelButton;
        private readonly Button _exportButton;
        private readonly NumericUpDown _maxRows;
        private readonly NumericUpDown _timeoutSeconds;
        private readonly DataGridView _results;
        private readonly ListView _historyList;
        private readonly Label _status;

        private CancellationTokenSource _runCts;
        private DaxQueryResult _currentResult = new DaxQueryResult();
        private bool _closing;

        public PbiBenchDaxWorkbenchForm(
            IDaxQueryExecutor executor,
            string connectionLabel,
            string unavailableReason)
        {
            _executor = executor;
            _connectionLabel = connectionLabel ?? string.Empty;
            _unavailableReason = unavailableReason ?? string.Empty;

            Text = "PbiBench DAX Workbench";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 600);
            Size = new Size(1220, 780);
            ShowInTaskbar = false;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.Dpi;

            var commandBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 42,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(8, 7, 8, 4)
            };

            _executeButton = new Button { Text = "Execute", AutoSize = true };
            _executeButton.Click += (s, e) => ExecuteQuery();
            _cancelButton = new Button { Text = "Cancel", AutoSize = true, Enabled = false };
            _cancelButton.Click += (s, e) => CancelQuery();
            _exportButton = new Button { Text = "Export CSV", AutoSize = true, Enabled = false };
            _exportButton.Click += (s, e) => ExportCsv();

            _maxRows = new NumericUpDown
            {
                Minimum = 1,
                Maximum = DaxQueryRequest.MaxAllowedRows,
                Value = DaxQueryRequest.DefaultMaxRows,
                ThousandsSeparator = true,
                Width = 88
            };
            _timeoutSeconds = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 3600,
                Value = DaxQueryRequest.DefaultTimeoutSeconds,
                Width = 68
            };

            commandBar.Controls.Add(_executeButton);
            commandBar.Controls.Add(_cancelButton);
            commandBar.Controls.Add(CreateInlineLabel("Rows"));
            commandBar.Controls.Add(_maxRows);
            commandBar.Controls.Add(CreateInlineLabel("Timeout (s)"));
            commandBar.Controls.Add(_timeoutSeconds);
            commandBar.Controls.Add(_exportButton);

            var clearHistory = new Button { Text = "Clear history", AutoSize = true };
            clearHistory.Click += (s, e) =>
            {
                _history.Clear();
                RefreshHistory();
                SetStatus("In-memory query history cleared.");
            };
            commandBar.Controls.Add(clearHistory);

            var vertical = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 270,
                Panel1MinSize = 150,
                Panel2MinSize = 220
            };

            _queryEditor = new RichTextBox
            {
                Dock = DockStyle.Fill,
                AcceptsTab = true,
                DetectUrls = false,
                Font = new Font(FontFamily.GenericMonospace, 10.0f),
                WordWrap = false,
                Text = "EVALUATE\nROW(\"Value\", 1)"
            };
            vertical.Panel1.Controls.Add(_queryEditor);

            var bottom = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = 830,
                Panel1MinSize = 500,
                Panel2MinSize = 240
            };

            _results = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToOrderColumns = true,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells,
                BackgroundColor = SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.CellSelect,
                VirtualMode = true
            };
            _results.CellValueNeeded += Results_CellValueNeeded;
            bottom.Panel1.Controls.Add(_results);

            _historyList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false,
                MultiSelect = false,
                ShowItemToolTips = true
            };
            _historyList.Columns.Add("Time", 72);
            _historyList.Columns.Add("State", 68);
            _historyList.Columns.Add("ms", 62);
            _historyList.Columns.Add("Query", 330);
            _historyList.DoubleClick += (s, e) => LoadSelectedHistory();
            bottom.Panel2.Controls.Add(_historyList);

            vertical.Panel2.Controls.Add(bottom);

            var footer = new Panel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(8, 3, 8, 3) };
            _status = new Label
            {
                Dock = DockStyle.Fill,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            footer.Controls.Add(_status);

            Controls.Add(vertical);
            Controls.Add(footer);
            Controls.Add(commandBar);

            FormClosing += OnWorkbenchClosing;
            ApplyExecutionAvailability();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _queryEditor.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.F5 || keyData == (Keys.Control | Keys.Enter))
            {
                if (_runCts == null) ExecuteQuery();
                return true;
            }

            if (keyData == Keys.Escape)
            {
                if (_runCts != null)
                {
                    CancelQuery();
                    return true;
                }

                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static Label CreateInlineLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Margin = new Padding(8, 6, 2, 0)
            };
        }

        private void ApplyExecutionAvailability()
        {
            var available = _executor != null;
            _executeButton.Enabled = available;

            if (available)
            {
                SetStatus("Ready · " + _connectionLabel + " · F5 / Ctrl+Enter execute · Esc cancel");
            }
            else
            {
                SetStatus("Draft mode · " + (string.IsNullOrWhiteSpace(_unavailableReason)
                    ? "No live query connection is available."
                    : _unavailableReason));
            }
        }

        private async void ExecuteQuery()
        {
            if (_executor == null || _runCts != null || _closing) return;

            var queryText = _queryEditor.Text;
            if (string.IsNullOrWhiteSpace(queryText))
            {
                SetStatus("Enter a DAX query before executing.");
                _queryEditor.Focus();
                return;
            }

            var request = new DaxQueryRequest
            {
                QueryText = queryText,
                MaxRows = Decimal.ToInt32(_maxRows.Value),
                TimeoutSeconds = Decimal.ToInt32(_timeoutSeconds.Value)
            };

            _runCts = new CancellationTokenSource();
            SetRunning(true);
            SetStatus("Running · " + _connectionLabel);

            try
            {
                var result = await _executor.ExecuteAsync(request, _runCts.Token);
                if (_closing || IsDisposed || Disposing) return;

                _currentResult = result ?? new DaxQueryResult
                {
                    State = DaxQueryExecutionState.Failed,
                    ErrorMessage = "The query provider returned no result."
                };

                BindResult(_currentResult);
                _history.Add(
                    queryText,
                    _currentResult.State == DaxQueryExecutionState.Completed,
                    Math.Max(0, _currentResult.DurationMilliseconds),
                    _currentResult.ErrorMessage);
                RefreshHistory();
                UpdateResultStatus(_currentResult);
            }
            catch (Exception ex)
            {
                if (_closing || IsDisposed || Disposing) return;

                _currentResult = new DaxQueryResult
                {
                    State = DaxQueryExecutionState.Failed,
                    ErrorMessage = string.IsNullOrWhiteSpace(ex.Message) ? "DAX query execution failed." : ex.Message
                };
                BindResult(_currentResult);
                _history.Add(queryText, false, 0, _currentResult.ErrorMessage);
                RefreshHistory();
                UpdateResultStatus(_currentResult);
            }
            finally
            {
                if (_runCts != null)
                {
                    _runCts.Dispose();
                    _runCts = null;
                }

                if (!_closing && !IsDisposed && !Disposing) SetRunning(false);
            }
        }

        private void CancelQuery()
        {
            if (_runCts == null || _runCts.IsCancellationRequested) return;
            SetStatus("Cancelling query…");
            _runCts.Cancel();
        }

        private void SetRunning(bool running)
        {
            _executeButton.Enabled = !running && _executor != null;
            _cancelButton.Enabled = running;
            _maxRows.Enabled = !running;
            _timeoutSeconds.Enabled = !running;
            _queryEditor.ReadOnly = running;
        }

        private void BindResult(DaxQueryResult result)
        {
            _results.RowCount = 0;
            _results.Columns.Clear();

            foreach (var column in result.Columns)
            {
                var name = string.IsNullOrWhiteSpace(column) ? "Column " + (_results.Columns.Count + 1) : column;
                _results.Columns.Add(new DataGridViewTextBoxColumn
                {
                    Name = "c" + _results.Columns.Count,
                    HeaderText = name,
                    SortMode = DataGridViewColumnSortMode.NotSortable
                });
            }

            _results.RowCount = result.Rows.Length;
            _results.Invalidate();
            _exportButton.Enabled = result.State == DaxQueryExecutionState.Completed
                && result.Columns.Length > 0;
        }

        private void Results_CellValueNeeded(object sender, DataGridViewCellValueEventArgs e)
        {
            var rows = _currentResult?.Rows;
            if (rows == null || e.RowIndex < 0 || e.RowIndex >= rows.Length) return;
            var row = rows[e.RowIndex];
            if (row == null || e.ColumnIndex < 0 || e.ColumnIndex >= row.Length) return;
            e.Value = row[e.ColumnIndex];
        }

        private void RefreshHistory()
        {
            _historyList.BeginUpdate();
            try
            {
                _historyList.Items.Clear();
                foreach (var entry in _history.Items)
                {
                    var preview = QueryPreview(entry.QueryText);
                    var item = new ListViewItem(entry.ExecutedUtc.ToLocalTime().ToString("HH:mm:ss"))
                    {
                        Tag = entry,
                        ToolTipText = entry.QueryText
                    };
                    item.SubItems.Add(entry.Succeeded ? "OK" : "Failed");
                    item.SubItems.Add(entry.DurationMilliseconds.ToString());
                    item.SubItems.Add(preview);
                    _historyList.Items.Add(item);
                }
            }
            finally
            {
                _historyList.EndUpdate();
            }
        }

        private void LoadSelectedHistory()
        {
            if (_historyList.SelectedItems.Count != 1) return;
            var entry = _historyList.SelectedItems[0].Tag as DaxQueryHistoryEntry;
            if (entry == null) return;

            _queryEditor.Text = entry.QueryText;
            _queryEditor.Focus();
            _queryEditor.SelectionStart = _queryEditor.TextLength;
            SetStatus("Loaded query from in-memory history · not executed.");
        }

        private void UpdateResultStatus(DaxQueryResult result)
        {
            switch (result.State)
            {
                case DaxQueryExecutionState.Completed:
                    SetStatus(
                        "Completed · " + result.ReturnedRowCount + " rows · " + result.DurationMilliseconds + " ms"
                        + (result.IsTruncated ? " · row limit reached; additional rows were not loaded" : string.Empty));
                    break;
                case DaxQueryExecutionState.Cancelled:
                    SetStatus("Cancelled · " + result.DurationMilliseconds + " ms · " + result.ErrorMessage);
                    break;
                default:
                    SetStatus("Failed · " + result.DurationMilliseconds + " ms · " + result.ErrorMessage);
                    break;
            }
        }

        private void ExportCsv()
        {
            if (_currentResult == null || _currentResult.Columns.Length == 0) return;

            using (var dialog = new SaveFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = "csv",
                AddExtension = true,
                FileName = "dax-query-results.csv"
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine(string.Join(",", _currentResult.Columns.Select(CsvCell)));
                    foreach (var row in _currentResult.Rows)
                        writer.WriteLine(string.Join(",", row.Select(v => CsvCell(Convert.ToString(v)))));
                }
            }

            SetStatus("Exported " + _currentResult.ReturnedRowCount + " rows to CSV.");
        }

        private static string CsvCell(string value)
        {
            value = value ?? string.Empty;
            var escaped = value.Replace("\"", "\"\"");
            return "\"" + escaped + "\"";
        }

        private static string QueryPreview(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return string.Empty;
            var preview = string.Join(" ", query
                .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => p.Length > 0));
            return preview.Length <= 120 ? preview : preview.Substring(0, 117) + "…";
        }

        private void SetStatus(string text)
        {
            _status.Text = text ?? string.Empty;
        }

        private void OnWorkbenchClosing(object sender, FormClosingEventArgs e)
        {
            _closing = true;
            if (_runCts != null && !_runCts.IsCancellationRequested)
                _runCts.Cancel();
        }
    }
}
