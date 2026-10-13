using System;
using System.Windows.Forms;
using PbiBench.Core.Dax;

namespace TabularEditor.PbiBench.Dax
{
    internal sealed partial class PbiBenchDaxWorkbenchForm
    {
        private ComboBox _resultSelector;
        private DaxQueryResult _executionResult;

        private void InitializeResultSelector(FlowLayoutPanel commands)
        {
            _resultSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            commands.Controls.Add(_resultSelector);
            _resultSelector.SelectedIndexChanged += (s, e) => SelectResult();
        }

        private void BindExecutionResult(DaxQueryResult result)
        {
            _executionResult = result;
            var tab = ActiveTab;
            if (tab != null) tab.Execution = result; // DAX-013: results stay with the tab that produced them
            _resultSelector.Items.Clear();
            for (var i = 0; i < result.ResultSets.Length; i++)
                _resultSelector.Items.Add("Result " + (i + 1) + " · " + result.ResultSets[i].ReturnedRowCount + " rows" +
                    (result.ResultSets[i].IsTruncated ? " +" : ""));
            if (_resultSelector.Items.Count > 0) _resultSelector.SelectedIndex = 0;
            else { _currentResult = result; BindResult(result); }
        }

        private void SelectResult()
        {
            var index = _resultSelector.SelectedIndex;
            if (_executionResult == null || index < 0 || index >= _executionResult.ResultSets.Length) return;
            var set = _executionResult.ResultSets[index];
            _currentResult = new DaxQueryResult { State = _executionResult.State, Columns = set.Columns, Rows = set.Rows,
                IsTruncated = set.IsTruncated, Reason = set.Reason, DurationMilliseconds = _executionResult.DurationMilliseconds };
            BindResult(_currentResult);
            SetStatus("Result " + (index + 1) + " of " + _executionResult.ResultSets.Length + " · " + set.ReturnedRowCount + " rows" +
                (set.IsTruncated ? " · incomplete (" + set.Reason + ")" : "") +
                (_executionResult.RemainingResultsNotRead ? " · remaining results not read (" + _executionResult.Reason + ")" : "") +
                " · Copy / Export CSV uses selected result.");
        }
    }
}
