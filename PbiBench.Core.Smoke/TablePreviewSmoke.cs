using System;
using PbiBench.Core.Dax;

internal static class TablePreviewSmoke
{
    internal static void Run()
    {
        var query = DaxTablePreview.Build("O'Brien", 12, "A]B", true, PreviewComparison.Equal, "\" ); EVALUATE 'secret' --", true);
        Check(query.Contains("'O''Brien'[A]]B] == \"\"\" ); EVALUATE 'secret' --\""), "Filter/name escaping failed.");
        Check(query.EndsWith("ORDER BY 'O''Brien'[A]]B] ASC"), "Sort missing.");
        Check(DaxTablePreview.Build("T", 1, "C", true, value: null).Contains("ISBLANK('T'[C])"), "Blank filter semantics.");
        Check(DaxTablePreview.Build("T", 1, "C", true, value: 1.25m).Contains("1.25"), "Invariant decimal.");
        Check(DaxTablePreview.Build("T", 1, "C", true, value: true).Contains("TRUE()"), "Typed boolean.");
        Reject(() => DaxTablePreview.Build("T", 0));
        Reject(() => DaxTablePreview.Build("T", 5001));
        Reject(() => DaxTablePreview.Build("T", 1, "C", true, value: new object()));
        Reject(() => DaxTablePreview.Build("T", 1, "C", true, (PreviewComparison)99));
        Console.WriteLine("Table preview: typed literals, adversarial escaping, sort and limits PASS");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Unsupported preview accepted."); }
}
