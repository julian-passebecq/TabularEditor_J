using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PbiBench.Core.Interop;
using Newtonsoft.Json.Linq;
using TabularEditor.PbiBench.Mcp;
using TabularEditor.TOMWrapper;

internal static class Te2LiveBridgeSmoke
{
    internal static void Run(TabularModelHandler handler, string companionExecutable = null)
    {
        var table = handler.Model.AddTable("Galaxy bridge synthetic");
        var target = table.AddMeasure("Unsaved target", "1");
        var manual = table.AddMeasure("Human note", "2");
        manual.Description = "Human edit before proposal";
        var selected = new List<ITabularNamedObject> { target };
        TabularModelHandler current = handler;
        EventHandler selectionChanged = null;
        using (var ui = new Form { Text = "Owned synthetic TE2 bridge host", Width = 700, Height = 400 })
        {
            var ownerClicks = 0;
            var ownerAction = new Button { Text = "Owned host action" };
            ownerAction.Click += (s, e) => ownerClicks++;
            ui.Controls.Add(ownerAction);
            ui.Show(); Application.DoEvents();
            if (!Environment.UserInteractive)
            {
                // Pure owned-form diagnostic only: no TOM, approval, guard override or product PASS.
                using (var modalProbe = new Form { Text = "Owned modal capability diagnostic" })
                {
                    modalProbe.Shown += (s, e) => modalProbe.Close();
                    try { modalProbe.ShowDialog(ui); Console.WriteLine("TE2 legacy modal diagnostic: UserInteractive=False; no exception observed; historical cause remains UNKNOWN"); }
                    catch (InvalidOperationException error) { Console.WriteLine("TE2 legacy modal diagnostic: UserInteractive=False; observed=" + error.GetType().Name + "; no product qualification inferred"); }
                }
            }
            using (var host = new PbiBenchLiveBridgeHost(ui, () => current, () => selected, false))
            {
                host.TrackSelection(h => selectionChanged += h, h => selectionChanged -= h);
                var acl = new PipeSecurity(); acl.SetSecurityDescriptorSddlForm(host.PipeSddl);
                var sid = WindowsIdentity.GetCurrent().User;
                Check(acl.AreAccessRulesProtected && acl.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<PipeAccessRule>().All(x => x.IdentityReference.Equals(sid) && x.AccessControlType == AccessControlType.Allow), "effective protected owner-only DACL");
                // Actual anonymous local client against the newly owned endpoint, not a simulated access check.
                var denied = Task.Run(() =>
                {
                    if (!ImpersonateAnonymousToken(GetCurrentThread())) throw new Exception("Anonymous token unavailable: " + Marshal.GetLastWin32Error());
                    try
                    {
                        using (var anonymous = new NamedPipeClientStream(".", host.PipeName, PipeDirection.InOut, PipeOptions.None, TokenImpersonationLevel.Anonymous))
                        { try { anonymous.Connect(1000); return false; } catch (UnauthorizedAccessException) { return true; } }
                    }
                    finally { if (!RevertToSelf()) throw new Exception("Anonymous token cleanup failed"); }
                });
                Check(Pump(denied), "real anonymous connection must be access denied");
                Console.WriteLine("TE2 live ACL: protected owner-only descriptor and REAL anonymous local client ACCESS_DENIED; token reverted PASS");

                Task.Run(() => { using (var malformed = Connect(host.PipeName)) { malformed.Write(new byte[] { 0, 0, 0, 1, 255 }, 0, 5); malformed.Flush(); } }).GetAwaiter().GetResult();
                foreach (var rejectedFrame in new[] { new byte[] { 0, 32, 0, 0 }, new byte[] { 0, 0, 0, 5, 123 } })
                    Task.Run(() => { using (var malformed = Connect(host.PipeName)) { malformed.Write(rejectedFrame, 0, rejectedFrame.Length); malformed.Flush(); } }).GetAwaiter().GetResult();

                var read = Pump(Task.Run(() => Send(host, Request(host, "read"))));
                Check(read["code"] == "OK" && host.LastModelReadThread == Thread.CurrentThread.ManagedThreadId, "same UI thread reads actual unsaved model");
                Check(!host.Completion.IsCompleted, "invalid UTF8 refused per connection; SAME endpoint still serves");
                RunActualStdio(host, companionExecutable);
                Check(read["snapshot"].Contains("Unsaved target") && read["snapshot"].Contains("\"IncludesExpressions\":false") && read["diskRevision"] == "UNKNOWN" && read["engineEvidence"].StartsWith("NOT_EXECUTED"), "unsaved metadata, withheld expressions and distinct engine provenance");
                var expressions = Request(host, "read"); expressions["includeExpressions"] = "true";
                Check(Pump(Task.Run(() => Send(host, expressions)))["code"] == "EXPRESSIONS_NOT_APPROVED", "host expression opt-in required");
                var mismatch = Request(host, "read"); mismatch["session"] = Guid.NewGuid().ToString("N");
                Check(Pump(Task.Run(() => Send(host, mismatch)))["code"] == "INSTANCE_SESSION_MISMATCH", "wrong session refused");
                mismatch = Request(host, "read"); mismatch["instance"] = Guid.NewGuid().ToString("N");
                Check(Pump(Task.Run(() => Send(host, mismatch)))["code"] == "INSTANCE_SESSION_MISMATCH", "wrong instance refused by actual host");
                var bad = Request(host, "read"); bad["rows"] = "true";
                Check(Pump(Task.Run(() => Send(host, bad)))["code"] == "INVALID_REQUEST", "row request refused");

                var proposal = Proposal(host, read, target, new Dictionary<string, string> { ["description"] = "Reviewed", ["expression"] = "3 + 4", ["folder"] = "Galaxy" });
                var undoBeforeReview = handler.UndoManager.UndoSize;
                using (var approve = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    approve.Tick += (s, e) =>
                    {
                        var form = Application.OpenForms.OfType<PbiBenchLiveBridgeReviewForm>().SingleOrDefault();
                        var button = form == null ? null : Descendants(form).OfType<Button>().SingleOrDefault(x => x.Name == "bridgeApprove");
                        if (button != null)
                        {
                            Check(Descendants(form).OfType<TextBox>().Any(x => x.AccessibleName == "Exact loaded model, table and measure target" && x.Text.Contains("Unsaved target")), "review visibly identifies exact target");
                            Check(ReferenceEquals(form.Owner, ui) && ui.OwnedForms.Contains(form), "review is attached to the exact living native owner Form");
                            Check(ui.Enabled && !form.Modal && target.Expression == "1" && handler.UndoManager.UndoSize == undoBeforeReview, "owned review is modeless and has no mutation or Undo before approval");
                            ownerAction.PerformClick();
                            Check(ownerClicks == 1, "owner UI action remains usable during live review");
                        }
                        button?.PerformClick();
                    };
                    approve.Start();
                    var response = Pump(Task.Run(() => Send(host, proposal))); approve.Stop();
                    Console.WriteLine("TE2 live review context: UserInteractive=" + Environment.UserInteractive + "; response=" + response["code"] + "; requestIdMatches=" + (response["requestId"] == proposal["requestId"]) + "; reviewSeen=" + (ownerClicks != 0) + "; knownSyntheticValuesApplied=" + (target.Description == "Reviewed" && target.Expression == "3 + 4" && target.DisplayFolder == "Galaxy") + "; UndoDelta=" + (handler.UndoManager.UndoSize - undoBeforeReview) + "; uiThread=" + Thread.CurrentThread.ManagedThreadId + "; uiContext=" + (SynchronizationContext.Current?.GetType().FullName ?? "NONE") + "; " + host.LastRefusalDiagnostic);
                    Check(response["code"] == "APPLIED_LOADED_MODEL_ONLY", "real host review button admits proposal; actual=" + response["code"] + "; UserInteractive=" + Environment.UserInteractive);
                }
                Check(target.Description == "Reviewed" && target.Expression == "3 + 4" && target.DisplayFolder == "Galaxy", "three typed properties readback");
                handler.UndoManager.Undo();
                Check((target.Description ?? "") == "" && target.Expression == "1" && (target.DisplayFolder ?? "") == "" && manual.Description == "Human edit before proposal", "one Undo restores all three and preserves previous human edit");

                var beforeModal = Pump(Task.Run(() => Send(host, Request(host, "read"))));
                var mutated = false;
                using (var staleModal = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    staleModal.Tick += (s, e) =>
                    {
                        var form = Application.OpenForms.OfType<PbiBenchLiveBridgeReviewForm>().SingleOrDefault();
                        if (form == null) return;
                        if (!mutated) { target.Description = "Concurrent human edit"; mutated = true; Descendants(form).OfType<Button>().Single(x => x.Name == "bridgeApprove").PerformClick(); }
                        else form.Close();
                    };
                    staleModal.Start(); var refused = Pump(Task.Run(() => Send(host, Proposal(host, beforeModal, target, new Dictionary<string, string> { ["description"] = "stale modal overwrite" })))); staleModal.Stop();
                    Check(mutated && refused["code"] == "NOT_APPLIED" && target.Description == "Concurrent human edit", "edit AFTER review opens refuses approval and preserves human value");
                }
                target.Description = "";

                var busy = new PbiBenchLiveBridgeProposal(handler, target, new Dictionary<string, string> { ["description"] = "busy forbidden" }, () => true);
                handler.BeginUpdate("Synthetic busy batch");
                try { try { busy.Apply(); throw new Exception("Busy batch admitted"); } catch (InvalidOperationException) { } }
                finally { handler.EndUpdate(rollback: true); }

                var fresh = Pump(Task.Run(() => Send(host, Request(host, "read"))));
                target.Description = "edit then revert"; target.Description = "";
                Check(Pump(Task.Run(() => Send(host, Proposal(host, fresh, target, new Dictionary<string, string> { ["description"] = "must not apply" }))))["code"] == "STALE", "edit-then-revert invalidates stamp");
                fresh = Pump(Task.Run(() => Send(host, Request(host, "read"))));
                selected.Clear(); selected.Add(manual); selectionChanged(null, EventArgs.Empty); selected.Clear(); selected.Add(target); selectionChanged(null, EventArgs.Empty);
                Check(Pump(Task.Run(() => Send(host, Proposal(host, fresh, target, new Dictionary<string, string> { ["description"] = "must not apply" }))))["code"] == "STALE", "selection away-and-back invalidates stamp");
                fresh = Pump(Task.Run(() => Send(host, Request(host, "read"))));
                current = null;
                Check(Pump(Task.Run(() => Send(host, Proposal(host, fresh, target, new Dictionary<string, string> { ["description"] = "must not apply" }))))["code"] != "APPLIED_LOADED_MODEL_ONLY", "absent/replaced handler refuses apply"); current = handler;

                // Direct typed adapter rollback test on the actual TOM setters; no UI approval assertion is inferred here.
                var changes = new Dictionary<string, string> { ["folder"] = "rollback", ["description"] = "reject setter" };
                var rollback = new PbiBenchLiveBridgeProposal(handler, target, changes, () => true);
                System.ComponentModel.PropertyChangingEventHandler reject = (s, e) => { if (e.PropertyName == "Description") throw new InvalidOperationException("synthetic setter rejection"); };
                target.PropertyChanging += reject;
                try { try { rollback.Apply(); throw new Exception("Expected setter refusal"); } catch (InvalidOperationException) { } }
                finally { target.PropertyChanging -= reject; }
                Check((target.DisplayFolder ?? "") == "" && (target.Description ?? "") == "" && handler.UndoManager.BatchDepth == 0, "rejected setter rolls back prior property");

                fresh = Pump(Task.Run(() => Send(host, Request(host, "read"))));
                var dropped = Proposal(host, fresh, target, new Dictionary<string, string> { ["description"] = "late forbidden" });
                Task.Run(() => { using (var peer = Connect(host.PipeName)) Te2LiveBridgeFraming.Write(peer, Te2LiveBridgeV1.Json(dropped)); }).GetAwaiter().GetResult();
                // Let the transport observe EOF while the UI callback is still queued, then pump it.
                Thread.Sleep(150); Application.DoEvents();
                Check((target.Description ?? "") == "", "disconnected queued request has no effect");

                fresh = Pump(Task.Run(() => Send(host, Request(host, "read"))));
                var cancelUndoSize = handler.UndoManager.UndoSize;
                PbiBenchLiveBridgeReviewForm cancelledReview = null;
                using (var stopReview = new System.Windows.Forms.Timer { Interval = 30 })
                {
                    stopReview.Tick += (s, e) =>
                    {
                        var form = Application.OpenForms.OfType<PbiBenchLiveBridgeReviewForm>().SingleOrDefault();
                        if (form == null || cancelledReview != null) return;
                        cancelledReview = form;
                        host.Dispose();
                        // A real button click after channel cancellation must still refuse mutation.
                        Descendants(form).OfType<Button>().Single(x => x.Name == "bridgeApprove").PerformClick();
                    };
                    stopReview.Start();
                    Pump(Task.Run(() => { try { Send(host, Proposal(host, fresh, target, new Dictionary<string, string> { ["description"] = "cancelled review must not apply" })); } catch (IOException) { } }));
                    stopReview.Stop();
                }
                Pump(Task.Delay(200));
                Check(cancelledReview != null && cancelledReview.IsDisposed && (target.Description ?? "") == "" && handler.UndoManager.UndoSize == cancelUndoSize && handler.UndoManager.BatchDepth == 0, "dispose DURING open review closes form with no apply or Undo even after approval click");
                host.Dispose(); host.Dispose(); Pump(host.Completion);
                Check(!CanConnect(host.PipeName), "pipe disappears after repeated dispose");
                Console.WriteLine("TE2 live actual compiled host: UI thread/unsaved/selection, modeless review + usable owner UI + real approval, three typed edits + ONE Undo, human edit preservation, stale edit-revert/selection/handler, rollback, disconnect, stop DURING review and repeated cleanup PASS; no Save/query/provider executed");
            }
            using (var closing = new PbiBenchLiveBridgeHost(ui, () => current, () => selected, false))
            {
                var queued = Task.Run(() => { try { Send(closing, Request(closing, "read")); } catch (IOException) { } });
                Check(SpinWait.SpinUntil(() => closing.PendingDispatches > 0, 2000), "UI dispatch queued without pumping");
                closing.Dispose();
                Check(closing.Completion.Wait(3000), "stop completes even when queued UI callback NEVER executes");
                queued.GetAwaiter().GetResult(); Application.DoEvents();
                Check(closing.LastModelReadThread == 0 && !CanConnect(closing.PipeName), "late callback after close does not touch TOM");
            }
            using (var detached = new Control())
            {
                var detachedHandle = detached.Handle;
                using (var ownerless = new PbiBenchLiveBridgeHost(detached, () => current, () => selected, false))
                {
                    var stamp = Pump(Task.Run(() => Send(ownerless, Request(ownerless, "read"))));
                    var undoBefore = handler.UndoManager.UndoSize;
                    var refused = Pump(Task.Run(() => Send(ownerless, Proposal(ownerless, stamp, target, new Dictionary<string, string> { ["description"] = "ownerless forbidden" }))));
                    Check(refused["code"] == "REFUSED" && ownerless.LastRefusalDiagnostic.Contains("phase=review-owner") && (target.Description ?? "") == "" && handler.UndoManager.UndoSize == undoBefore, "ownerless UI control refuses review without mutation or Undo");
                    ownerless.Dispose(); Pump(ownerless.Completion);
                    Check(!CanConnect(ownerless.PipeName), "ownerless refused endpoint cleanup");
                }
            }
            ui.Close();
        }
        table.Delete();
    }
    private static void RunActualStdio(PbiBenchLiveBridgeHost host, string companionExecutable)
    {
        var exe = companionExecutable ?? Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../TE2Enhanced.Mcp/bin/Release/net8.0/TE2Enhanced.Mcp.exe"));
        Check(File.Exists(exe), "actual built stdio companion required");
        using (var child = Process.Start(new ProcessStartInfo { FileName = exe, Arguments = "--live \"" + host.Descriptor + "\"", UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true }))
        {
            try
            {
                var roundtrip = Task.Run(() =>
                {
                    child.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"tools/list\"}"); child.StandardInput.Flush();
                    var tools = JObject.Parse(child.StandardOutput.ReadLine());
                    Check(((JArray)tools["result"]["tools"]).Count == 8, "actual stdio live tools selected");
                    child.StandardInput.WriteLine("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/call\",\"params\":{\"name\":\"te2_live_read\",\"arguments\":{}}}"); child.StandardInput.Flush();
                    var result = JObject.Parse(child.StandardOutput.ReadLine());
                    Check((bool)result["result"]["isError"] == false && (string)result["result"]["structuredContent"]["instance"] == host.Instance && result["result"]["structuredContent"]["snapshot"]["Selection"].Any(), "ACTUAL stdio executable -> selected ACL IPC -> compiled host UI -> current selection");
                    child.StandardInput.Close();
                    Check(child.WaitForExit(3000) && child.ExitCode == 0, "stdio EOF graceful cleanup");
                    Check(child.StandardError.ReadToEnd().Contains("LIVE instance"), "truthful live banner");
                });
                Pump(roundtrip);
                Console.WriteLine("TE2 live end-to-end: ACTUAL built stdio companion -> owner ACL local IPC -> compiled host UI/same unsaved selection PASS; child EOF exit0");
            }
            finally { if (!child.HasExited) { child.Kill(); child.WaitForExit(); } }
        }
    }
    private static Dictionary<string, string> Request(PbiBenchLiveBridgeHost host, string action) => new Dictionary<string, string> { ["schema"] = Te2LiveBridgeV1.Schema, ["requestId"] = Guid.NewGuid().ToString("N"), ["instance"] = host.Instance, ["session"] = host.Session, ["action"] = action };
    private static Dictionary<string, string> Proposal(PbiBenchLiveBridgeHost host, Dictionary<string, string> read, Measure m, IDictionary<string, string> changes)
    {
        var result = Request(host, "propose"); foreach (var key in new[] { "modelId", "revision", "selection" }) result[key] = read[key];
        result["table"] = m.Table.Name; result["measure"] = m.Name; result["changes"] = Te2LiveBridgeV1.Json(changes); return result;
    }
    private static NamedPipeClientStream Connect(string name)
    { var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification); client.Connect(2000); return client; }
    private static Dictionary<string, string> Send(PbiBenchLiveBridgeHost host, Dictionary<string, string> request)
    { using (var peer = Connect(host.PipeName)) { Te2LiveBridgeFraming.Write(peer, Te2LiveBridgeV1.Json(request)); return Te2LiveBridgeV1.Parse(Te2LiveBridgeFraming.Read(peer), Te2LiveBridgeV1.ResponseFields); } }
    private static bool CanConnect(string name) { try { using (var p = new NamedPipeClientStream(".", name, PipeDirection.InOut)) p.Connect(150); return true; } catch (TimeoutException) { return false; } }
    private static T Pump<T>(Task<T> task) { Pump((Task)task); return task.GetAwaiter().GetResult(); }
    private static void Pump(Task task)
    { var time = Stopwatch.StartNew(); while (!task.IsCompleted && time.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(5); } if (!task.IsCompleted) throw new TimeoutException("Owned host smoke deadline"); task.GetAwaiter().GetResult(); }
    private static IEnumerable<Control> Descendants(Control parent) { foreach (Control child in parent.Controls) { yield return child; foreach (var inner in Descendants(child)) yield return inner; } }
    private static void Check(bool value, string label) { if (!value) throw new Exception(label); }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool ImpersonateAnonymousToken(IntPtr thread);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool RevertToSelf();
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentThread();
}
