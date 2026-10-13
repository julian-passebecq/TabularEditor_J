using System;
using System.IO;
using System.Linq;
using PbiBench.Core.Snapshot;
using TabularEditor.PbiBench.Mcp;
using TabularEditor.TOMWrapper;

// MCP companion seam: host projection into the neutral snapshot, explicit opt-in, no mutation.
internal static class SnapshotPublisherSmoke
{
    public static void Run(TabularModelHandler handler)
    {
        var model = handler.Model;
        var table = model.Tables.First();
        var measure = table.Measures.First();

        var plain = PbiBenchModelSnapshotPublisher.Build(model, new ITabularNamedObject[] { measure }, false, new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc));
        Check(plain.Tables.Count == model.Tables.Count, "table count");
        Check(plain.Tables.SelectMany(t => t.Measures).All(m => m.Expression == null), "no expressions without opt-in");
        Check(plain.Selection.Single().Kind == "Measure" && plain.Selection[0].Table == table.Name, "selection projection");
        Check(plain.PublishedAtUtc == "2026-10-13T00:00:00Z" && plain.ModelRevision.StartsWith("sha256:"), "identity stamps");

        var again = PbiBenchModelSnapshotPublisher.Build(model, null, false, DateTime.UtcNow);
        var withSelection = PbiBenchModelSnapshotPublisher.Build(model, new ITabularNamedObject[] { measure }, false, DateTime.UtcNow);
        Check(withSelection.ModelRevision == plain.ModelRevision && again.ModelRevision != plain.ModelRevision, "revision tracks published content only");

        var full = PbiBenchModelSnapshotPublisher.Build(model, null, true, DateTime.UtcNow);
        Check(full.Tables.SelectMany(t => t.Measures).Any(m => m.Expression == measure.Expression), "expressions after opt-in");

        var dir = Path.Combine(Path.GetTempPath(), "te2e-snapshot-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = Path.Combine(dir, ModelSnapshotSerializer.FileName);
            // D16: the destination defaults to the per-user path, is shown, and can be changed (Save As);
            // the smoke never publishes to the real per-user location.
            using (var defaultForm = new PbiBenchSnapshotPublishForm(() => model, () => null))
            {
                var handle = defaultForm.Handle;
                Check(defaultForm.IsDefaultPath && defaultForm.Path == Path.GetFullPath(PbiBenchModelSnapshotPublisher.DefaultPath()), "default destination unchanged");
                Check(Descendants(defaultForm).OfType<System.Windows.Forms.TextBox>().Any(t => t.Text == defaultForm.Path), "destination shown");
                Check(Descendants(defaultForm).OfType<System.Windows.Forms.Button>().Any(b => b.Text.Replace("&", "").StartsWith("Save As")), "Save As button present");
                defaultForm.Path = Path.Combine(dir, "custom", "chosen.json");
                Check(!defaultForm.IsDefaultPath && Descendants(defaultForm).OfType<System.Windows.Forms.TextBox>().Any(t => t.Text == defaultForm.Path), "destination changeable and shown");
                Check(defaultForm.Publish() && File.Exists(Path.Combine(dir, "custom", "chosen.json")), "publish to chosen destination");
            }
            using (var form = new PbiBenchSnapshotPublishForm(() => model, () => new ITabularNamedObject[] { measure }, path))
            {
                var handle = form.Handle;
                Check(!form.IncludeExpressions, "dialog defaults to no expressions");
                Check(form.Publish() && File.Exists(path), "dialog publishes atomically");
                var read = ModelSnapshotSerializer.ReadFile(path);
                Check(read.ModelRevision == form.LastPublished.ModelRevision && !read.IncludesExpressions, "round trip");
                var original = measure.Description;
                measure.Description = "Server=x;Password=secret";
                try
                {
                    var before = File.ReadAllText(path);
                    Check(!form.Publish() && File.ReadAllText(path) == before, "credential-looking text refused, previous snapshot kept");
                }
                finally { measure.Description = original; }
            }
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        Check(handler.UndoManager.BatchDepth == 0 && measure.Description != "Server=x;Password=secret", "publisher opened no undo batch; test edit restored");
        Console.WriteLine("MCP snapshot publisher: projection/opt-in/revision/atomic/credential guard PASS");
    }

    private static System.Collections.Generic.IEnumerable<System.Windows.Forms.Control> Descendants(System.Windows.Forms.Control parent)
    {
        foreach (System.Windows.Forms.Control child in parent.Controls)
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }

    private static void Check(bool condition, string what)
    {
        if (!condition) throw new Exception("Snapshot publisher check failed: " + what);
    }
}
