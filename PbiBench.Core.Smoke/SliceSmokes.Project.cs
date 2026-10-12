// S5 Project core smokes.
internal static partial class SliceSmokes
{
    static partial void Project(string fixtureRoot)
    {
        ReviewedFilePlanSmoke.Run();
        ProjectIdentitySmoke.Run(fixtureRoot);
        ExternalChangeSmoke.Run();
        VsCodeArgsSmoke.Run();
    }
}
