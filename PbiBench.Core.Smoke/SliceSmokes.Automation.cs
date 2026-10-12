using System;
using System.IO;
using System.Linq;
using PbiBench.Core.Documents;

// S4 Automation core smokes: RecoveryStore, ScriptDocument/ScriptDiagnostic, RecipeCatalog/MetadataRecipe.
internal static partial class SliceSmokes
{
    static partial void Automation(string fixtureRoot)
    {
        RecoveryStoreSmoke(fixtureRoot);
    }

    private static void RecoveryStoreSmoke(string fixtureRoot)
    {
        var root = Path.Combine(fixtureRoot, "recovery-synthetic");
        var t0 = new DateTime(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc);

        var disabled = new RecoveryStore(new RecoveryStoreOptions(root, enabled: false));
        AutoCheck(disabled.Save("doc-1", "csharp", "Script 1", "Output(1);", t0) == null, "Disabled store must not save.");
        AutoCheck(!Directory.Exists(root) && disabled.List().Count == 0, "Disabled store must not touch disk.");
        AutoCheck(RecoveryStoreOptions.DefaultRoot.EndsWith(Path.Combine("TE2Enhanced", "recovery"), StringComparison.OrdinalIgnoreCase), "Default root must be per-user TE2Enhanced\\recovery.");

        var store = new RecoveryStore(new RecoveryStoreOptions(root, enabled: true, retentionDays: 7, maxFiles: 3));
        AutoCheck(store.Disclosure.Contains(root) && store.Disclosure.Contains("never uploaded"), "Disclosure must name the folder and privacy.");
        var entry = store.Save("doc-1", "csharp", "Script\u0007 1", "// synthetic\r\nOutput(\"é\");", t0)!;
        AutoCheck(entry.IsIntact && entry.Title == "Script 1" && entry.Sha256.Length == 64, "Save metadata wrong.");
        AutoCheck(store.Read("doc-1") == "// synthetic\r\nOutput(\"é\");", "Round trip changed content.");
        AutoCheck(Directory.GetFiles(root, "*.tmp-*").Length == 0, "Atomic write left temp files.");

        // Overwrite keeps one snapshot per document.
        store.Save("doc-1", "csharp", "Script 1", "Output(2);", t0.AddMinutes(1));
        AutoCheck(store.List().Count == 1 && store.Read("doc-1") == "Output(2);", "Overwrite must replace the snapshot.");
        AutoCheck(Directory.GetFiles(root, "doc-1.*").Length == 2, "Older version files must be removed after a new version is complete.");

        // Tamper: hash manifest detects modified content.
        File.WriteAllText(Directory.GetFiles(root, "doc-1.*" + RecoveryStore.ContentExtension).Single(), "tampered");
        AutoCheck(!store.List().Single().IsIntact, "Tampered content must be reported as not intact.");
        AutoThrows<InvalidDataException>(() => store.Read("doc-1"), "Tampered content must not be returned.");

        // Retention by count: newest 3 kept.
        for (var i = 2; i <= 5; i++) store.Save("doc-" + i, "dax", "Doc " + i, "EVALUATE ROW(\"x\", " + i + ")", t0.AddMinutes(10 + i));
        var ids = store.List().Select(e => e.DocumentId).ToArray();
        AutoCheck(ids.SequenceEqual(new[] { "doc-5", "doc-4", "doc-3" }), "Count retention must keep the newest files: " + string.Join(",", ids));

        // Retention by age.
        AutoCheck(store.Prune(t0.AddDays(8)) == 3 && store.List().Count == 0, "Age retention must remove expired snapshots.");

        // Explicit delete; unrelated files are untouched; ids cannot traverse paths.
        store.Save("keep", "csharp", "Keep", "x", t0);
        var unrelated = Path.Combine(root, "notes.txt");
        File.WriteAllText(unrelated, "user file");
        AutoCheck(store.Delete("keep") && !store.Delete("keep"), "Explicit delete failed.");
        store.Save("a", "csharp", "A", "1", t0); store.Save("b", "csharp", "B", "2", t0);
        AutoCheck(store.DeleteAll() == 2 && File.Exists(unrelated), "DeleteAll must remove only store-owned files.");
        AutoThrows<ArgumentException>(() => store.Save("..\\escape", "csharp", "x", "x", t0), "Path traversal id must be rejected.");
        AutoThrows<ArgumentException>(() => store.Save("ok", "csharp", "x", "x", DateTime.Now), "Local timestamps must be rejected.");
        AutoThrows<ArgumentException>(() => new RecoveryStore(new RecoveryStoreOptions(root, true, maxContentChars: 4)).Save("big", "c", "t", "12345", t0), "Oversized content must be rejected.");
        Console.WriteLine("RecoveryStoreSmoke: opt-in, atomic write, hash manifest, retention, explicit delete PASS");
    }

    private static void AutoCheck(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void AutoThrows<TException>(Action action, string message) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException(message);
    }
}
