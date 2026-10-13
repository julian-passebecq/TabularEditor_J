using System;
using TabularEditor.PbiBench.Dax;
using TabularEditor.PbiBench.Semantic;
using TabularEditor.PbiBench.Navigation;
using TabularEditor.TOMWrapper;
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        int failures = 0;
        Probe("DAX Workbench", () => new PbiBenchDaxWorkbenchForm(null, "", "Audit offline probe"), ref failures);
        using (var handler = new TabularModelHandler())
        {
            var snapshot = PbiBenchSemanticSnapshot.Build(handler.Model);
            Probe("Quick Open", () => new PbiBenchQuickOpenForm(snapshot.Index.Descriptors), ref failures);
            Probe("Semantic View", () => new PbiBenchSemanticViewForm(snapshot), ref failures);
        }
        return failures;
    }
    private static void Probe(string name, Func<System.Windows.Forms.Form> create, ref int failures)
    {
        try { using (var form = create()) Console.WriteLine(name + " constructor: PASS"); }
        catch (Exception ex) { Console.WriteLine(name + " constructor: FAIL\n" + ex); failures++; }
    }
}
