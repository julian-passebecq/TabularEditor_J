using System;
using System.IO;
using System.Linq;
using System.Text;
using PbiBench.Core.Project;

internal static class ReviewedFilePlanSmoke
{
    internal static void Run()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "te2-plan-smoke-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(sandbox, "project");
        var journals = Path.Combine(sandbox, "journal");
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "a.json"), "A1");
            File.WriteAllText(Path.Combine(root, "b.json"), "B1");
            File.WriteAllText(Path.Combine(root, "c.json"), "C1");
            Func<string, string> read = name => File.ReadAllText(Path.Combine(root, name));

            // Path validation.
            foreach (var bad in new[] { "..\\x.json", "C:\\x.json", "sub\\..\\..\\x", "", "a/./b" })
                Expect(() => new ReviewedFilePlan(root, "bad").AddFromCurrent(bad, new byte[0]), "path " + bad);
            var journalInside = new ReviewedFilePlan(root, "inside");
            journalInside.AddFromCurrent("a.json", Bytes("A2"));
            Expect(() => journalInside.Apply(Path.Combine(root, ".journal")), "journal inside project");

            // Stale rejection: nothing written.
            var stale = new ReviewedFilePlan(root, "stale");
            stale.AddFromCurrent("a.json", Bytes("A2"));
            stale.AddFromCurrent("b.json", Bytes("B2"));
            File.WriteAllText(Path.Combine(root, "b.json"), "B-external");
            if (stale.Preview().Count(p => p.IsStale) != 1) throw new Exception("Preview missed the stale file.");
            try { stale.Apply(journals); throw new Exception("Stale plan applied."); }
            catch (ReviewedFilePlanStaleException ex) { if (ex.Stale.Single().RelativePath != "b.json") throw; }
            if (read("a.json") != "A1" || read("b.json") != "B-external") throw new Exception("Stale rejection wrote files.");
            var created = new ReviewedFilePlan(root, "create-stale");
            created.Add(new ReviewedFileChange("new.json", ReviewedFileChangeKind.Create, null, Bytes("N")));
            File.WriteAllText(Path.Combine(root, "new.json"), "racing");
            Expect(() => created.Apply(journals), "create over a racing file");
            File.Delete(Path.Combine(root, "new.json"));

            // Failure on file 2 of 3 restores file 1 (and file 3 is never touched).
            var failing = new ReviewedFilePlan(root, "fail-2");
            failing.AddFromCurrent("a.json", Bytes("A2"));
            failing.AddFromCurrent("b.json", Bytes("B2"));
            failing.Add(new ReviewedFileChange("sub/d.json", ReviewedFileChangeKind.Create, null, Bytes("D2")));
            try
            {
                failing.Apply(journals, new ReviewedFilePlanOptions { BeforeCommit = (i, p) => { if (i == 1) throw new IOException("Injected failure"); } });
                throw new Exception("Injected failure did not surface.");
            }
            catch (ReviewedFilePlanApplyException ex)
            {
                if (!ex.RolledBack || ReviewedFilePlan.ReadJournal(ex.JournalPath).State != ReviewedFileJournalState.RolledBack) throw new Exception("Rollback not recorded.");
            }
            if (read("a.json") != "A1" || read("b.json") != "B-external" || File.Exists(Path.Combine(root, "sub", "d.json")))
                throw new Exception("Failure on file 2 did not restore file 1.");
            if (Directory.GetFiles(root, "*.te2tmp-*", SearchOption.AllDirectories).Length != 0) throw new Exception("Temp files leaked.");

            // Failure on file 3 (a create) after two replaces restores both.
            // Each scenario uses its own files: Windows scanners can refuse an atomic replace of a file
            // replaced milliseconds earlier; the plan reports that as a real failure, it is not smoke noise.
            File.WriteAllText(Path.Combine(root, "a3.json"), "A1");
            File.WriteAllText(Path.Combine(root, "b3.json"), "B1");
            var failing3 = new ReviewedFilePlan(root, "fail-3");
            failing3.AddFromCurrent("a3.json", Bytes("A2"));
            failing3.AddFromCurrent("b3.json", Bytes("B2"));
            failing3.AddFromCurrent("sub/d3.json", Bytes("D2"));
            try { failing3.Apply(journals, new ReviewedFilePlanOptions { BeforeCommit = (i, p) => { if (i == 2) throw new IOException("Injected"); } }); throw new Exception("No failure"); }
            catch (ReviewedFilePlanApplyException ex) when (ex.RolledBack) { }
            if (read("a3.json") != "A1" || read("b3.json") != "B1" || File.Exists(Path.Combine(root, "sub", "d3.json"))) throw new Exception("Failure on file 3 did not restore files 1-2.");

            // Successful apply: replace, create, delete; then journal restore.
            File.WriteAllText(Path.Combine(root, "a4.json"), "A1");
            var plan = new ReviewedFilePlan(root, "apply");
            plan.AddFromCurrent("a4.json", Bytes("A3"));
            plan.AddFromCurrent("sub/d4.json", Bytes("D3"));
            plan.Add(new ReviewedFileChange("c.json", ReviewedFileChangeKind.Delete, ReviewedFilePlan.HashFile(Path.Combine(root, "c.json")), null));
            var result = plan.Apply(journals);
            if (result.FilesWritten != 3 || read("a4.json") != "A3" || read("sub/d4.json") != "D3" || File.Exists(Path.Combine(root, "c.json")))
                throw new Exception("Apply did not write the reviewed files.");
            if (!ReviewedFilePlan.ListJournals(journals).Contains(result.JournalPath)) throw new Exception("Journal not listed.");

            // Restore refuses when a file changed after apply.
            File.WriteAllText(Path.Combine(root, "a4.json"), "A-later");
            Expect(() => ReviewedFilePlan.Restore(result.JournalPath), "restore over later edit");
            if (read("a4.json") != "A-later" || read("sub/d4.json") != "D3") throw new Exception("Refused restore wrote files.");
            File.WriteAllText(Path.Combine(root, "a4.json"), "A3");
            if (ReviewedFilePlan.Restore(result.JournalPath) != 3) throw new Exception("Restore count wrong.");
            if (read("a4.json") != "A1" || read("c.json") != "C1" || File.Exists(Path.Combine(root, "sub", "d4.json")))
                throw new Exception("Journal restore did not bring back the originals.");
            if (ReviewedFilePlan.ReadJournal(result.JournalPath).State != ReviewedFileJournalState.Restored) throw new Exception("Restore not recorded.");
            Expect(() => ReviewedFilePlan.Restore(result.JournalPath), "double restore");
            Console.WriteLine("ReviewedFilePlanSmoke: stale reject, failure 2/3 + 3/3 rollback, journal restore PASS");
        }
        finally
        {
            try { Directory.Delete(sandbox, true); } catch (Exception) { }
        }
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    internal static void Expect(Action action, string label)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new Exception("Expected refusal: " + label);
    }
}
