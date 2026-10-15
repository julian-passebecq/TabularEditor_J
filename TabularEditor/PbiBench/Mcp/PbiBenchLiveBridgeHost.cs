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
using Microsoft.Win32.SafeHandles;
using PbiBench.Core.Interop;
using PbiBench.Core.Snapshot;
using TabularEditor.TOMWrapper;

namespace TabularEditor.PbiBench.Mcp
{
    /// <summary>Owned, opt-in Windows local IPC. Transport never reads TOM; all model work is dispatched to the host UI.</summary>
    internal sealed class PbiBenchLiveBridgeHost : IDisposable
    {
        private readonly Control _ui;
        private readonly Func<TabularModelHandler> _handler;
        private readonly Func<IEnumerable<ITabularNamedObject>> _selection;
        private readonly Func<long> _context;
        private readonly bool _expressions;
        private readonly PbiBenchLiveBridgeRevision _revision = new PbiBenchLiveBridgeRevision();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly string _ownerSid;
        private readonly object _pipeLock = new object();
        private NamedPipeServerStream _pipe;
        private bool _disposed;
        private int _pendingDispatches;
        private Action _unsubscribe;
        internal readonly string PipeName = "te2-galaxy-" + Guid.NewGuid().ToString("N");
        internal readonly string Instance = Guid.NewGuid().ToString("N");
        internal readonly string Session = Guid.NewGuid().ToString("N");
        internal string Descriptor => PipeName + "|" + Instance + "|" + Session;
        internal Task Completion { get; }
        internal string PipeSddl { get; }
        internal int LastModelReadThread { get; private set; }
        internal int PendingDispatches => Volatile.Read(ref _pendingDispatches);
        internal PbiBenchLiveBridgeHost(Control ui, Func<TabularModelHandler> handler, Func<IEnumerable<ITabularNamedObject>> selection, bool expressions, Func<long> context = null)
        {
            if (ui == null || ui.IsDisposed || !ui.IsHandleCreated || ui.InvokeRequired) throw new InvalidOperationException("UI_HOST_REQUIRED");
            _ui = ui; _handler = handler; _selection = selection; _expressions = expressions; _context = context ?? (() => 0);
            _ownerSid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("OWNER_REQUIRED");
            _pipe = CreatePipe(); // ACL in place before descriptor is exposed or listener starts.
            PipeSddl = _pipe.GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
            ui.Disposed += UiDisposed;
            Completion = Task.Run(Listen);
        }
        internal void TrackSelection(Action<EventHandler> subscribe, Action<EventHandler> unsubscribe)
        { _revision.AssertUi(); subscribe(_revision.SelectionChanged); _unsubscribe = () => unsubscribe(_revision.SelectionChanged); }
        private NamedPipeServerStream CreatePipe()
        {
            var sid = new SecurityIdentifier(_ownerSid);
            var acl = new PipeSecurity(); acl.SetAccessRuleProtection(true, false); acl.SetOwner(sid);
            acl.AddAccessRule(new PipeAccessRule(sid, PipeAccessRights.FullControl, AccessControlType.Allow));
            var bytes = acl.GetSecurityDescriptorBinaryForm();
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                var attributes = new SecurityAttributes { Length = Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor = pinned.AddrOfPinnedObject(), Inherit = false };
                // FILE_FLAG_FIRST_PIPE_INSTANCE | FILE_FLAG_OVERLAPPED | PIPE_ACCESS_DUPLEX;
                // PIPE_REJECT_REMOTE_CLIENTS is a transport refusal, independent of the owner/session checks.
                var handle = CreateNamedPipe("\\\\.\\pipe\\" + PipeName, 0x00080000u | 0x40000000u | 3u, 8u, 1, 4096, 4096, 0, ref attributes);
                if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("PIPE_CREATE_REFUSED", new System.ComponentModel.Win32Exception(error)); }
                try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
                catch { handle.Dispose(); throw; }
            }
            finally { pinned.Free(); }
        }
        private async Task Listen()
        {
            while (!_stop.IsCancellationRequested)
            {
                NamedPipeServerStream pipe;
                Task peer = null;
                lock (_pipeLock) { pipe = _pipe; }
                try
                {
                    await Task.Run(() => pipe.WaitForConnection()).ConfigureAwait(false);
                    using (var requestLife = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                    using (requestLife.Token.Register(() => { try { pipe.Dispose(); } catch (ObjectDisposedException) { } }))
                    {
                        requestLife.CancelAfter(TimeSpan.FromSeconds(120));
                        RequireLocalOwner(pipe);
                        var json = await Task.Run(() => Te2LiveBridgeFraming.Read(pipe)).ConfigureAwait(false);
                        Dictionary<string, string> request;
                        try { request = Te2LiveBridgeV1.ParseRequest(json); }
                        catch (Exception) { Te2LiveBridgeFraming.Write(pipe, Te2LiveBridgeV1.Json(Response("", "INVALID_REQUEST"))); continue; }
                        // One frame per connection. EOF or unexpected additional input revokes a queued review.
                        peer = Task.Run(() => { try { pipe.ReadByte(); } catch (IOException) { } catch (ObjectDisposedException) { } finally { try { requestLife.Cancel(); } catch (ObjectDisposedException) { } } });
                        var result = await OnUi(() => Handle(request, requestLife.Token), requestLife.Token).ConfigureAwait(false);
                        if (!requestLife.IsCancellationRequested) Te2LiveBridgeFraming.Write(pipe, Te2LiveBridgeV1.Json(result));
                    }
                }
                catch (Exception error) when (error is InvalidDataException || error is ArgumentException || error is IOException || error is ObjectDisposedException || error is OperationCanceledException || error is UnauthorizedAccessException || error is InvalidOperationException)
                { /* This connection is refused/closed. No logging of request content or automatic mutation retry. */ }
                finally
                {
                    pipe.Dispose();
                    if (peer != null) await peer.ConfigureAwait(false);
                    lock (_pipeLock) { if (!_stop.IsCancellationRequested) _pipe = CreatePipe(); }
                }
            }
        }
        private void RequireLocalOwner(NamedPipeServerStream pipe)
        {
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid == 0 || Process.GetProcessById((int)pid).SessionId != Process.GetCurrentProcess().SessionId) throw new UnauthorizedAccessException("LOCAL_SESSION_REQUIRED");
            string sid = null;
            pipe.RunAsClient(() => { using (var identity = WindowsIdentity.GetCurrent()) { if (!identity.IsAnonymous) sid = identity.User?.Value; } });
            if (sid != _ownerSid) throw new UnauthorizedAccessException("OWNER_REQUIRED");
        }
        private Task<Dictionary<string, string>> OnUi(Func<Task<Dictionary<string, string>>> action, CancellationToken token)
        {
            var result = new TaskCompletionSource<Dictionary<string, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Increment(ref _pendingDispatches);
            var registration = token.Register(() => result.TrySetCanceled());
            result.Task.ContinueWith(t => { registration.Dispose(); Interlocked.Decrement(ref _pendingDispatches); }, TaskScheduler.Default);
            try
            {
                _ui.BeginInvoke(new Action(async () =>
                {
                    try { token.ThrowIfCancellationRequested(); if (_disposed) throw new OperationCanceledException(); result.TrySetResult(await action()); }
                    catch (Exception) { result.TrySetResult(Response("", "REFUSED")); }
                }));
            }
            catch (InvalidOperationException) { result.TrySetCanceled(); }
            return result.Task;
        }
        private async Task<Dictionary<string, string>> Handle(Dictionary<string, string> request, CancellationToken token)
        {
            _revision.AssertUi(); LastModelReadThread = Thread.CurrentThread.ManagedThreadId;
            var id = request["requestId"];
            if (_disposed || token.IsCancellationRequested) return Response(id, "CLOSED");
            if (request["instance"] != Instance || request["session"] != Session) return Response(id, "INSTANCE_SESSION_MISMATCH");
            try
            {
                var handler = _handler();
                var snapshot = _revision.Capture(handler, _selection(), _expressions && request.TryGetValue("includeExpressions", out var include) && include == "true", out var revision, out var selection, _context());
                var result = Response(id, "OK");
                result["modelId"] = _revision.ModelId; result["revision"] = revision; result["selection"] = selection;
                result["diskRevision"] = "UNKNOWN"; result["loadedRevision"] = revision; result["liveRevision"] = "UNKNOWN"; result["gitRevision"] = "UNKNOWN";
                result["engineEvidence"] = "NOT_EXECUTED: loaded/unsaved metadata is not evidence from a query engine";
                if (request["action"] == "read")
                {
                    if (request.TryGetValue("includeExpressions", out include) && include == "true" && !_expressions) return Response(id, "EXPRESSIONS_NOT_APPROVED");
                    result["snapshot"] = ModelSnapshotSerializer.Serialize(snapshot); return result;
                }
                Func<bool> current = () =>
                {
                    if (_disposed || token.IsCancellationRequested || !ReferenceEquals(_handler(), handler)) return false;
                    _revision.Capture(handler, _selection(), false, out var nowRevision, out var nowSelection, _context());
                    return request["modelId"] == _revision.ModelId && nowRevision == request["revision"] && nowSelection == request["selection"];
                };
                if (!current()) return Response(id, "STALE");
                var table = handler.Model.Tables.FirstOrDefault(t => t.Name == request["table"]);
                var measure = table?.Measures.FirstOrDefault(m => m.Name == request["measure"]);
                if (measure == null) return Response(id, "TARGET_ABSENT");
                var proposal = new PbiBenchLiveBridgeProposal(handler, measure, Te2LiveBridgeV1.Changes(request), () => current() && !measure.IsRemoved && ReferenceEquals(table.Measures.FirstOrDefault(m => m.Name == request["measure"]), measure));
                using (var form = new PbiBenchLiveBridgeReviewForm(proposal, token))
                {
                    // Keep the owned TE2 UI usable while the request waits for a real host decision.
                    // No modal service-desktop bypass or message pumping is used by the product.
                    var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    form.FormClosed += (sender, args) => closed.TrySetResult(true);
                    form.Show(_ui);
                    await closed.Task;
                }
                _revision.AssertUi();
                return Response(id, proposal.Applied ? "APPLIED_LOADED_MODEL_ONLY" : "NOT_APPLIED");
            }
            catch (Exception) { return Response(id, "REFUSED"); }
        }
        private Dictionary<string, string> Response(string id, string code) => new Dictionary<string, string>
        { ["schema"] = Te2LiveBridgeV1.Schema, ["requestId"] = id, ["code"] = code, ["instance"] = Instance, ["session"] = Session };
        private void UiDisposed(object sender, EventArgs e) => Dispose();
        public void Dispose()
        {
            if (_disposed) return; _revision.AssertUi(); _disposed = true;
            _stop.Cancel(); lock (_pipeLock) { _pipe?.Dispose(); }
            _unsubscribe?.Invoke(); _ui.Disposed -= UiDisposed; _revision.Dispose();
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint pid);
        [StructLayout(LayoutKind.Sequential)]
        private struct SecurityAttributes { internal int Length; internal IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] internal bool Inherit; }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafePipeHandle CreateNamedPipe(string name, uint openMode, uint pipeMode, uint maxInstances, uint outBuffer, uint inBuffer, uint timeout, ref SecurityAttributes attributes);
    }
}
