// Core smoke extension points; each area implements its partial in SliceSmokes.<Area>.cs.
internal static partial class SliceSmokes
{
    public static void Run(string fixtureRoot)
    {
        Dax(fixtureRoot); DaxWorkspace(fixtureRoot); Automation(fixtureRoot);
        Project(fixtureRoot); Report(fixtureRoot); Explore(fixtureRoot);
    }

    static partial void Dax(string fixtureRoot);
    static partial void DaxWorkspace(string fixtureRoot);
    static partial void Automation(string fixtureRoot);
    static partial void Project(string fixtureRoot);
    static partial void Report(string fixtureRoot);
    static partial void Explore(string fixtureRoot);
}
