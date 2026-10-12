using TabularEditor.TOMWrapper;

// Smoke extension points. Each area implements its partials in SliceSmokes.<Area>.cs.
// Early hooks run against the shared handler before fixtures that replace/dispose the
// singleton handler; late hooks run after all historical fixtures.
internal static partial class SliceSmokes
{
    public static void RunEarly(TabularModelHandler handler)
    {
        EarlyDax(handler); EarlyDaxWorkspace(handler); EarlyAutomation(handler);
        EarlyProject(handler); EarlyReport(handler); EarlyExplore(handler);
    }

    public static void RunLate()
    {
        LateDax(); LateDaxWorkspace(); LateAutomation(); LateProject(); LateReport(); LateExplore();
    }

    static partial void EarlyDax(TabularModelHandler handler);
    static partial void EarlyDaxWorkspace(TabularModelHandler handler);
    static partial void EarlyAutomation(TabularModelHandler handler);
    static partial void EarlyProject(TabularModelHandler handler);
    static partial void EarlyReport(TabularModelHandler handler);
    static partial void EarlyExplore(TabularModelHandler handler);
    static partial void LateDax();
    static partial void LateDaxWorkspace();
    static partial void LateAutomation();
    static partial void LateProject();
    static partial void LateReport();
    static partial void LateExplore();
}
