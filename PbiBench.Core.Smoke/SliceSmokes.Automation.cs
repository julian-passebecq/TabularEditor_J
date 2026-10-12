using System;
using System.IO;
using System.Linq;
using PbiBench.Core.Automation;
using PbiBench.Core.Documents;

// S4 Automation core smokes: RecoveryStore, ScriptDocument/ScriptDiagnostic, RecipeCatalog/MetadataRecipe.
internal static partial class SliceSmokes
{
    static partial void Automation(string fixtureRoot)
    {
        RecoveryStoreSmoke(fixtureRoot);
        ScriptDocumentSmoke();
        RecipeCatalogSmoke();
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

    private static void ScriptDocumentSmoke()
    {
        var workspace = new ScriptWorkspace();
        var first = workspace.New();
        var second = workspace.New("Output(Model.Tables.Count);");
        AutoCheck(first.Title == "Script 1" && second.Title == "Script 2" && RecoveryStore.IsValidDocumentId(first.Id), "Workspace titles/ids wrong.");
        AutoCheck(!first.IsDirty && second.IsDirty && workspace.Close(first.Id, false) && !workspace.Close(second.Id, false), "Dirty close guard wrong.");

        // Trust is bound to the exact reviewed content; edits revoke it; stale confirmations are refused.
        var reviewed = second.ContentHash;
        AutoCheck(!second.IsTrusted, "New document must start untrusted.");
        second.ConfirmTrust(reviewed);
        AutoCheck(second.IsTrusted, "Confirmed trust not recorded.");
        var generation = second.Generation;
        AutoCheck(second.SetText("Output(1);") && !second.IsTrusted && second.Generation == generation + 1, "Edit must revoke trust and bump generation.");
        AutoThrows<InvalidOperationException>(() => second.ConfirmTrust(reviewed), "Trust for stale content must be refused.");
        second.MarkSaved("C:\\synthetic\\script.csx");
        AutoCheck(!second.IsDirty && second.FilePath != null, "MarkSaved did not clear dirty state.");
        AutoCheck(ScriptDocument.TrustStatement.Contains("NOT sandboxed") && !ScriptDocument.TrustStatement.Contains("safe"), "Trust statement must not claim sandboxing.");
        AutoThrows<ArgumentException>(() => second.SetText(new string('x', ScriptDocument.MaxChars + 1)), "Oversized script must be rejected.");
        for (var i = workspace.Documents.Count; i < ScriptWorkspace.MaxDocuments; i++) workspace.New();
        AutoThrows<InvalidOperationException>(() => workspace.New(), "Workspace must be bounded.");

        // Diagnostics: map in-range lines, hoisted/generated lines become 0, errors first, bounded, stale by generation.
        var doc = new ScriptDocument("diag", "Diag", "var x = 1;\nOutput(y);\n// File.Delete(\"x\") in a comment\nSystem.IO.File.WriteAllText(\"a\", \"b\");");
        var messages = new[]
        {
            new CompilerMessage(true, "CS0219", "The variable 'x' is assigned but its value is never used", 1, 5),
            new CompilerMessage(false, "CS0103", "The name 'y' does not exist in the current context", 2, 8),
            new CompilerMessage(false, "CS1002", "; expected\r\n", 57, 3)
        };
        var diags = ScriptDiagnostics.FromCompiler(doc, messages);
        AutoCheck(diags.Count == 3 && diags[0].Code == "CS0103" && diags[0].Location == "Ln 2, Col 8", "Errors must sort first with mapped location.");
        AutoCheck(diags[1].Line == 0 && diags[1].Location == "(generated code)" && diags[1].Message == "; expected", "Out-of-range line must map to generated code.");
        AutoCheck(diags[2].Severity == ScriptDiagnosticSeverity.Warning && diags.All(d => d.Generation == doc.Generation), "Warning/generation wrong.");
        AutoCheck(ScriptDiagnostics.Summary(diags) == "2 errors, 1 warning", "Summary wrong: " + ScriptDiagnostics.Summary(diags));
        AutoCheck(ScriptDiagnostics.FromCompiler(doc, Enumerable.Repeat(messages[1], 1000)).Count == ScriptDiagnostics.MaxDiagnostics, "Diagnostics must be bounded.");
        var advisory = ScriptDiagnostics.Advisory(doc);
        AutoCheck(advisory.Count == 1 && advisory[0].Line == 4 && advisory[0].IsAdvisory && advisory[0].Message.Contains("not a security check"),
            "Advisory scan must skip comments and be labelled honestly.");
        Console.WriteLine("ScriptDocumentSmoke: tabs, dirty guard, hash-bound trust, diagnostics mapping, advisory labelling PASS");
    }

    private static void RecipeCatalogSmoke()
    {
        AutoCheck(RecipeCatalog.All.Select(r => r.PropertyName).SequenceEqual(new[] { "DisplayFolder", "IsHidden", "FormatString", "Description", "Name" }), "Catalog properties changed.");
        AutoCheck(RecipeCatalog.All.Select(r => r.Id).Distinct().Count() == RecipeCatalog.All.Count && RecipeCatalog.Find("measure.rename")!.SingleTargetOnly, "Catalog ids not unique or rename not single-target.");
        var fs = RecipeCatalog.For(MetadataRecipeOperation.SetFormatString);
        AutoCheck(fs.Validate("#,0.00") == null && fs.Validate("\"$\"#,0") == null && fs.Validate("\\\"x") == null, "Valid format strings rejected.");
        AutoCheck(fs.Validate("\"$#,0") != null && fs.Validate("0\n0") != null, "Invalid format strings accepted.");
        var desc = RecipeCatalog.For(MetadataRecipeOperation.SetDescription);
        AutoCheck(desc.Validate("Line 1\r\nLine 2\tend") == null && desc.Validate("bell\u0007") != null && desc.Validate(new string('d', 4001)) != null, "Description validator wrong.");
        var name = RecipeCatalog.For(MetadataRecipeOperation.Rename);
        AutoCheck(name.Validate("Net Sales") == null && name.Validate("") != null && name.Validate(" Sales") != null && name.Validate("a\tb") != null, "Name validator wrong.");

        var source = new[]
        {
            new MeasureMetadata("[Sales]", "Sales", "", false, "", "", false),
            new MeasureMetadata("[Margin]", "Margin", "KPI", false, "0.0%", "", false),
            new MeasureMetadata("[Dynamic]", "Dynamic", "", false, "", "", true)
        };
        var plan = MetadataRecipe.Propose(source, new[] { "[Sales]", "[Margin]" }, MetadataRecipeOperation.SetFormatString, "0.0%");
        AutoCheck(plan.Changes.Count == 1 && plan.Changes[0].Id == "[Sales]" && plan.Changes[0].Property == "FormatString", "Per-property diff wrong.");
        AutoThrows<ArgumentException>(() => MetadataRecipe.Propose(source, new[] { "[Dynamic]" }, MetadataRecipeOperation.SetFormatString, "0"), "Dynamic format string target must be refused.");
        AutoThrows<ArgumentException>(() => MetadataRecipe.Propose(source, new[] { "[Sales]", "[Margin]" }, MetadataRecipeOperation.Rename, "X", new RenameContext(new string[0], new RenameDependent[0], true)), "Rename must be single-target.");

        // Rename preview mirrors TE2 FormulaFixup: CR removed, every tracked span replaced, qualified spans become [New].
        var expr = "DIVIDE([Margin],\r\n 'Fact'[Sales])";
        var stripped = expr.Replace("\r", "");
        var s1 = stripped.IndexOf("'Fact'[Sales]", StringComparison.Ordinal);
        var dependents = new[] { new RenameDependent("[Margin %]", "Expression", expr, new[] { new ReferenceSpan(s1, s1 + "'Fact'[Sales]".Length - 1, true) }) };
        var context = new RenameContext(new[] { "Amount" }, dependents, true);
        var rename = MetadataRecipe.Propose(source, new[] { "[Sales]" }, MetadataRecipeOperation.Rename, "Net ]Sales", context);
        AutoCheck(rename.Changes.Single().After == "Net ]Sales" && rename.DependentRewrites.Single().After == "DIVIDE([Margin],\n [Net ]]Sales])", "Rename rewrite preview wrong: " + rename.DependentRewrites.Single().After);
        AutoCheck(MetadataRecipe.IsCurrent(rename, source, context), "Fresh rename plan must be current.");
        var edited = new RenameContext(new[] { "Amount" }, new[] { new RenameDependent("[Margin %]", "Expression", expr + " ", dependents[0].Spans) }, true);
        AutoCheck(!MetadataRecipe.IsCurrent(rename, source, edited), "Dependent edit must make the rename plan stale.");
        AutoThrows<ArgumentException>(() => MetadataRecipe.Propose(source, new[] { "[Sales]" }, MetadataRecipeOperation.Rename, "amount", context), "Collision must be refused.");
        AutoThrows<ArgumentException>(() => MetadataRecipe.Propose(source, new[] { "[Sales]" }, MetadataRecipeOperation.Rename, "margin", context), "Collision with another measure must be refused.");
        AutoThrows<ArgumentException>(() => MetadataRecipe.Propose(source, new[] { "[Sales]" }, MetadataRecipeOperation.Rename, "New", new RenameContext(new string[0], dependents, false)), "Rename with fix-up off must be refused.");
        var untracked = new[] { new RenameDependent("[Text]", "Expression", "\"[Sales]\" & [Sales]", new[] { new ReferenceSpan(12, 18, false) }) };
        AutoThrows<ArgumentException>(() => MetadataRecipe.Propose(source, new[] { "[Sales]" }, MetadataRecipeOperation.Rename, "New", new RenameContext(new string[0], untracked, true)), "Untracked textual reference must refuse rename.");
        AutoCheck(MetadataRecipe.CountTextualReferences("[sales] + [SALES]", "Sales") == 2, "Textual count must be case-insensitive.");
        Console.WriteLine("RecipeCatalogSmoke: typed validators, per-property diff, rename preview/stale/refusals PASS");
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
