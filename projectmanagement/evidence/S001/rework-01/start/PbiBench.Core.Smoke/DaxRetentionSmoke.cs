using System;
using PbiBench.Core.Dax;

internal static class DaxRetentionSmoke
{
    internal static void Run()
    {
        void Check(bool ok) { if (!ok) throw new Exception("Neutral retained-result contract failed."); }
        Check(new DaxQueryRequest().MaxRetainedBytes == 32L * 1024 * 1024);
        Check(DaxResultRetention.MaxColumns == 256 && DaxResultRetention.MaxCellBytes == 1024 * 1024);
        Check(DaxResultRetention.TryEstimate("界😀", out var size) && size == 30);
        Check(DaxResultRetention.TryEstimate(new byte[16], out size) && size == 48);
        Check(DaxResultRetention.TryEstimate(DBNull.Value, out size) && size == 0);
        Check(DaxResultRetention.TryEstimate(1m, out size) && size == 24);
        Check(!DaxResultRetention.TryEstimate(new object(), out size));
        foreach (var bad in new[] { 0L, -1L, 32L * 1024 * 1024 + 1 })
        {
            try { new DaxQueryRequest { MaxRetainedBytes = bad }; throw new Exception("Budget accepted outside bounds."); }
            catch (ArgumentOutOfRangeException) { }
        }
        var history = new DaxQueryHistory();
        history.Add("synthetic", new DaxQueryResult { IsTruncated = true, Reason = DaxQueryReason.MemoryLimit });
        Check(history.Items[0].IsTruncated && history.Items[0].Reason == DaxQueryReason.MemoryLimit);
    }
}
