using System;
using System.IO;
using System.Linq;
using System.Text;
using PbiBench.Core.Project;

// S5 Project core smokes.
internal static partial class SliceSmokes
{
    static partial void Project(string fixtureRoot)
    {
        // Review-fix fixtures are written first so the files they replace are not brand new when applied (OBS-001).
        var fixes = ReviewFixFixture.Prepare();
        try
        {
            ReviewedFilePlanSmoke.Run();
            ProjectIdentitySmoke.Run(fixtureRoot);
            ExternalChangeSmoke.Run();
            VsCodeArgsSmoke.Run();
            QaFixSmokes.Project();
            ReviewFixFixture.Twice(fixes, "ReviewedFilePlanReviewFixesSmoke", ReviewedFilePlanReviewFixesSmoke.Run);
            ReviewFixFixture.Twice(fixes, "JournalRetentionSmoke", JournalRetentionSmoke.Run);
        }
        finally { ReviewFixFixture.Delete(fixes.Sandbox); }
    }
}

/// <summary>Two identical fixture sets; a scenario that hits OBS-001 (replace refused, rolled back) is repeated once on the spare set.</summary>
internal static class ReviewFixFixture
{
    internal sealed class Sets { internal string Sandbox = "", A = "", B = ""; }
    internal sealed class Obs001Exception : Exception { internal Obs001Exception(Exception inner) : base("OBS-001", inner) { } }

    private static readonly string[] Files = { "e1", "e2", "e3", "f1", "f2", "g1", "m1", "m2", "r-applied", "r-restored", "r-rolled", "r-applying", "r-single" };

    internal static Sets Prepare()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "te2-review-fixes-" + Guid.NewGuid().ToString("N"));
        var sets = new Sets { Sandbox = sandbox, A = Path.Combine(sandbox, "a", "project"), B = Path.Combine(sandbox, "b", "project") };
        foreach (var root in new[] { sets.A, sets.B })
        {
            Directory.CreateDirectory(root);
            foreach (var name in Files) File.WriteAllText(Path.Combine(root, name + ".json"), name.ToUpperInvariant());
        }
        return sets;
    }

    internal static void Twice(Sets sets, string name, Action<string, string> scenario)
    {
        try { scenario(sets.A, Path.Combine(sets.Sandbox, "a", "journal-" + name)); }
        catch (Obs001Exception)
        {
            Console.WriteLine(name + ": OBS-001 observed (replace refused by another process, rolled back); repeating once on the spare fixture");
            scenario(sets.B, Path.Combine(sets.Sandbox, "b", "journal-" + name));
        }
    }

    /// <summary>Rethrows an apply failure caused by OBS-001 as <see cref="Obs001Exception"/> (after its rollback succeeded).</summary>
    internal static void ThrowIfObs001(ReviewedFilePlanApplyException ex)
    {
        if (!ex.RolledBack || ex.InnerException is ReviewedFileChangedExternallyException) return;
        for (var e = ex.InnerException; e != null; e = e.InnerException)
            if (e is IOException && ((e.HResult & 0xFFFF) == 1175 || e.Message.IndexOf("to be replaced", StringComparison.OrdinalIgnoreCase) >= 0)) throw new Obs001Exception(ex);
    }

    internal static ReviewedFilePlanResult Apply(ReviewedFilePlan plan, string journals, ReviewedFilePlanOptions? options = null)
    {
        try { return plan.Apply(journals, options); }
        catch (ReviewedFilePlanApplyException ex) { ThrowIfObs001(ex); throw; }
    }

    internal static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    internal static void Delete(string path) { try { Directory.Delete(path, true); } catch (Exception) { } }
}

/// <summary>Architect review #2 (re-hash right before each commit) and #3 (marker failure after commit is a warning, not a refusal).</summary>
internal static class ReviewedFilePlanReviewFixesSmoke
{
    internal static void Run(string root, string journals)
    {
        Func<string, string> read = name => File.ReadAllText(Path.Combine(root, name));
        Action<string, string> write = (name, text) => File.WriteAllText(Path.Combine(root, name), text);
        Func<string, byte[]> bytes = ReviewFixFixture.Bytes;

        // #2: an external save to file 2 after the stale check (during apply) fails the apply; file 1 is rolled back,
        // the external edit is kept, file 3 is never touched, and the message names the changed file.
        var plan = new ReviewedFilePlan(root, "external-during-apply");
        plan.AddFromCurrent("e1.json", bytes("E1-new"));
        plan.AddFromCurrent("e2.json", bytes("E2-new"));
        plan.AddFromCurrent("e3.json", bytes("E3-new"));
        try
        {
            plan.Apply(journals, new ReviewedFilePlanOptions { BeforeCommit = (i, p) => { if (i == 1) write("e2.json", "E2-external"); } });
            throw new Exception("An external change during apply was overwritten.");
        }
        catch (ReviewedFilePlanApplyException ex)
        {
            ReviewFixFixture.ThrowIfObs001(ex);
            Check(ex.RolledBack && ex.InnerException is ReviewedFileChangedExternallyException inner && inner.RelativePath == "e2.json", "external change must fail and roll back: " + ex.Message);
            Check(ex.Message.Contains("e2.json") && ex.Message.Contains("changed by another program"), "message must name the externally changed file: " + ex.Message);
            Check(ReviewedFilePlan.ReadJournal(ex.JournalPath).State == ReviewedFileJournalState.RolledBack, "rollback must be recorded");
        }
        Check(read("e1.json") == "E1" && read("e2.json") == "E2-external" && read("e3.json") == "E3", "file 1 restored, external edit kept, file 3 untouched");

        // Same for a delete target, and for a create target that appears during apply.
        var deletes = new ReviewedFilePlan(root, "external-before-delete");
        deletes.AddFromCurrent("f1.json", bytes("F1-new"));
        deletes.Add(new ReviewedFileChange("f2.json", ReviewedFileChangeKind.Delete, ReviewedFilePlan.HashFile(Path.Combine(root, "f2.json")), null));
        deletes.Add(new ReviewedFileChange("f3.json", ReviewedFileChangeKind.Create, null, bytes("F3")));
        try
        {
            deletes.Apply(journals, new ReviewedFilePlanOptions { BeforeCommit = (i, p) => { if (i == 1) write("f2.json", "F2-external"); } });
            throw new Exception("An externally edited delete target was deleted.");
        }
        catch (ReviewedFilePlanApplyException ex) { ReviewFixFixture.ThrowIfObs001(ex); Check(ex.RolledBack && ex.Message.Contains("f2.json"), "delete target: " + ex.Message); }
        Check(read("f1.json") == "F1" && read("f2.json") == "F2-external" && !File.Exists(Path.Combine(root, "f3.json")), "delete target edited externally is kept");
        var creates = new ReviewedFilePlan(root, "external-create");
        creates.AddFromCurrent("g1.json", bytes("G1-new"));
        creates.Add(new ReviewedFileChange("g2.json", ReviewedFileChangeKind.Create, null, bytes("G2")));
        try
        {
            creates.Apply(journals, new ReviewedFilePlanOptions { BeforeCommit = (i, p) => { if (i == 1) write("g2.json", "G2-external"); } });
            throw new Exception("A file created externally during apply was overwritten.");
        }
        catch (ReviewedFilePlanApplyException ex) { ReviewFixFixture.ThrowIfObs001(ex); Check(ex.RolledBack && ex.Message.Contains("g2.json"), "create target: " + ex.Message); }
        Check(read("g1.json") == "G1" && read("g2.json") == "G2-external", "externally created file is kept");

        // #3: every file committed, then the Applied marker cannot be written: the apply still succeeds with a warning,
        // the journal reads as interrupted and Restore still works.
        var marked = new ReviewedFilePlan(root, "marker-fails");
        marked.AddFromCurrent("m1.json", bytes("M1-new"));
        var result = ReviewFixFixture.Apply(marked, journals, new ReviewedFilePlanOptions { BeforeMark = s => throw new IOException("synthetic marker failure") });
        Check(result.State == ReviewedFileJournalState.Applied && result.FilesWritten == 1 && result.Warning.Contains("synthetic marker failure") && read("m1.json") == "M1-new",
            "marker failure after commit must return Applied with a warning");
        Check(ReviewedFilePlan.ReadJournal(result.JournalPath).State == ReviewedFileJournalState.Applying, "journal without marker reads as interrupted");
        Check(ReviewedFilePlan.Restore(result.JournalPath) == 1 && read("m1.json") == "M1", "restore must still work after a marker failure");
        var clean = new ReviewedFilePlan(root, "marker-ok");
        clean.AddFromCurrent("m2.json", bytes("M2-new"));
        Check(ReviewFixFixture.Apply(clean, journals).Warning.Length == 0, "a normal apply has no warning");
        Check(Directory.GetFiles(root, "*.te2tmp-*", SearchOption.AllDirectories).Length == 0, "no temp files leaked");
        Console.WriteLine("ReviewedFilePlanReviewFixesSmoke: re-hash before each commit (replace/delete/create), external edit kept + rollback, marker failure = applied with warning PASS");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Review fix: " + message); }
}

/// <summary>V1 gap 3: retention for %LOCALAPPDATA%\TE2Enhanced\journal (list, prune completed older than N days, explicit delete).</summary>
internal static class JournalRetentionSmoke
{
    internal static void Run(string root, string journals)
    {
        var sandbox = Path.GetDirectoryName(root)!;
        Func<string, string> apply = name =>
        {
            var plan = new ReviewedFilePlan(root, name);
            plan.AddFromCurrent(name, ReviewFixFixture.Bytes("after"));
            return ReviewFixFixture.Apply(plan, journals).JournalPath;
        };
        Check(ReviewedFilePlan.ListJournalInfos(journals).Count == 0 && ReviewedFilePlan.PruneJournals(journals, DateTime.UtcNow) == 0, "missing folder lists nothing");
        var applied = apply("r-applied.json");
        var restored = apply("r-restored.json");
        ReviewedFilePlan.Restore(restored);
        var rolledPlan = new ReviewedFilePlan(root, "r-rolled");
        rolledPlan.AddFromCurrent("r-rolled.json", ReviewFixFixture.Bytes("after"));
        string rolled;
        try { rolledPlan.Apply(journals, new ReviewedFilePlanOptions { BeforeCommit = (i, p) => throw new IOException("Injected") }); throw new Exception("no failure"); }
        catch (ReviewedFilePlanApplyException ex) { rolled = ex.JournalPath; }
        var applyingPlan = new ReviewedFilePlan(root, "r-applying");
        applyingPlan.AddFromCurrent("r-applying.json", ReviewFixFixture.Bytes("after"));
        var applying = ReviewFixFixture.Apply(applyingPlan, journals, new ReviewedFilePlanOptions { BeforeMark = s => throw new IOException("marker") }).JournalPath;
        var corruptDir = Path.Combine(journals, "29990101-000000-corrupt0");
        Directory.CreateDirectory(corruptDir);
        File.WriteAllText(Path.Combine(corruptDir, "journal.json"), "{ not json");

        var infos = ReviewedFilePlan.ListJournalInfos(journals);
        Check(infos.Count == 5, "five journals listed: " + infos.Count);
        Check(infos.Single(i => i.JournalPath == applied).State == ReviewedFileJournalState.Applied && infos.Single(i => i.JournalPath == restored).IsCompleted &&
            infos.Single(i => i.JournalPath == rolled).State == ReviewedFileJournalState.RolledBack && !infos.Single(i => i.JournalPath == applying).IsCompleted,
            "states read from markers");
        Check(infos.Single(i => i.Directory == corruptDir).Error.Length > 0 && !infos.Single(i => i.Directory == corruptDir).IsCompleted, "unreadable journal listed with an error");
        Check(infos.All(i => i.TotalBytes > 0 && i.LastActivityUtc > DateTime.UtcNow.AddHours(-1)), "sizes and activity times");

        var now = DateTime.UtcNow;
        Check(ReviewedFilePlan.PruneJournals(journals, now.AddDays(29)) == 0, "journals younger than 30 days are kept");
        Check(ReviewedFilePlan.SelectPrunableJournals(journals, now.AddDays(31)).Count == 3, "only completed journals are prunable");
        Check(ReviewedFilePlan.PruneJournals(journals, now.AddDays(31)) == 3, "prune removes completed journals older than 30 days");
        Check(!Directory.Exists(Path.GetDirectoryName(applied)) && !Directory.Exists(Path.GetDirectoryName(restored)) && !Directory.Exists(Path.GetDirectoryName(rolled)),
            "pruned journal folders are removed");
        Check(File.Exists(applying) && File.Exists(Path.Combine(corruptDir, "journal.json")), "Applying and unreadable journals are never pruned");
        Check(ReviewedFilePlan.PruneJournals(journals, now.AddDays(400), 365) == 0, "custom retention still skips incomplete journals");
        ReviewedFilePlanSmoke.Expect(() => ReviewedFilePlan.SelectPrunableJournals(journals, now, 0), "retention of 0 days");
        ReviewedFilePlanSmoke.Expect(() => ReviewedFilePlan.SelectPrunableJournals(journals, DateTime.Now), "local timestamp");

        // Explicit delete of one: completed only, inside the journal folder only; unrelated files stay.
        ReviewedFilePlanSmoke.Expect(() => ReviewedFilePlan.DeleteJournal(journals, applying), "delete an Applying journal");
        ReviewedFilePlanSmoke.Expect(() => ReviewedFilePlan.DeleteJournal(journals, Path.Combine(corruptDir, "journal.json")), "delete an unreadable journal");
        var single = apply("r-single.json");
        ReviewedFilePlanSmoke.Expect(() => ReviewedFilePlan.DeleteJournal(Path.Combine(sandbox, "elsewhere"), single), "delete outside the journal folder");
        var stray = Path.Combine(Path.GetDirectoryName(single)!, "user-notes.txt");
        File.WriteAllText(stray, "not a journal file");
        ReviewedFilePlan.DeleteJournal(journals, single);
        Check(!File.Exists(single) && File.Exists(stray) && Directory.GetFiles(Path.GetDirectoryName(single)!).Length == 1, "explicit delete removes only journal-owned files");
        Check(ReviewedFilePlan.ListJournalInfos(journals).Count == 2, "two journals left (applying + unreadable)");
        Console.WriteLine("JournalRetentionSmoke: list with states, prune completed > 30 days, never Applying/unreadable, explicit delete PASS");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception("Journal retention: " + message); }
}
