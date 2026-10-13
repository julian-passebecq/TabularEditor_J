using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using PbiBench.Core.Dax;
using TabularEditor.PbiBench.Dax;
using static QueryLifecycleSmoke;

internal static class QaAdversarial
{
    [STAThread] static int Main()
    {
        try
        {
            Application.EnableVisualStyles();
            using (var entered = new ManualResetEventSlim())
            using (var release = new ManualResetEventSlim())
            using (var user = new CancellationTokenSource())
            {
                var first = new Session { OnDispose = () => { entered.Set(); Assert(release.Wait(10000), "QA cleanup watchdog"); } };
                var second = new Session();
                int factories = 0;
                var executor = new PbiBenchAmoDaxQueryExecutor(() => ++factories == 1 ? first : second);
                var task = executor.ExecuteAsync(new DaxQueryRequest { QueryText = "synthetic" }, user.Token);
                Wait(() => entered.IsSet);
                user.Cancel();
                Assert(!task.IsCompleted, "Cleanup released terminal result early");
                var overlap = executor.ExecuteAsync(new DaxQueryRequest { QueryText = "overlap" }, CancellationToken.None);
                Assert(overlap.Result.State == DaxQueryExecutionState.Failed && factories == 1, "Cleanup admitted another session");
                release.Set(); Wait(() => task.IsCompleted);
                Assert(task.Result.Reason == DaxQueryReason.UserCancellation, "Cancellation during cleanup lost");
                var next = executor.ExecuteAsync(new DaxQueryRequest { QueryText = "next" }, CancellationToken.None);
                Wait(() => next.IsCompleted);
                Assert(next.Result.State == DaxQueryExecutionState.Completed && factories == 2 && second.Cancels == 0, "Later session hit by stale cancellation/admission leak");
                Assert(first.Disposes == 1 && second.Disposes == 1, "Duplicate disposal");
                Console.WriteLine("QA-T05: held Dispose, overlap denial, cancellation at cleanup and next-session isolation PASS");
            }
            var bytes = new byte[] { 1, 2, 3 };
            var session = new Session(); session.Reader.Value = bytes;
            var binary = new PbiBenchAmoDaxQueryExecutor(() => session).ExecuteAsync(new DaxQueryRequest { QueryText = "binary" }, CancellationToken.None);
            Wait(() => binary.IsCompleted); bytes[0] = 99;
            Assert(((byte[])binary.Result.Rows[0][0])[0] == 1, "Retained binary aliases provider buffer");
            Console.WriteLine("QA-T07: provider binary mutation cannot alter retained result PASS");
            var folder = Path.Combine(Path.GetTempPath(), "pbibench-qa-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var path = Path.Combine(folder, "synthetic.dax");
                var document = new DaxDocument(); document.New("// synthetic"); document.Save(path);
                for (int i = 0; i < 50; i++)
                {
                    document.Text = "// synthetic revision " + i;
                    document.Save(path);
                    Assert(File.ReadAllText(path) == document.Text && !document.IsDirty, "Document replacement lost content/state");
                }
                Assert(Directory.GetFiles(folder).Length == 1, "Document temporary file leak");
                Console.WriteLine("QA-T09: 50 sequential atomic document replacements PASS; prior IOException not reproduced");
            }
            finally { foreach (var path in Directory.GetFiles(folder)) File.Delete(path); Directory.Delete(folder); }
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
