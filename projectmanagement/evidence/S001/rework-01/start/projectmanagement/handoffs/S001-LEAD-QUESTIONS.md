# S001 provider deadline / live-validation decision packet

Owner for decision: technical lead. Developer completed available implementation
and controlled tests; this packet is not acceptance.

## R-001 / DAX-001: hard provider deadline remains unproven

Expected: cancellation/timeout leaves the UI responsive and keeps the draft and
query ownership intact. A token alone must not be advertised as a hard deadline.

Implemented: capture the request and cloned connection identity before dispatch.
One dedicated AMO session per admitted run. Configure Connect Timeout and Timeout
in the dedicated connection string. Signal cancellation from the token callback
by scheduling one worker; retry CancelCommand while the operation is active to
cover cancellation before the command actually starts. Join that worker before
reader/session disposal. Do not release admission until cleanup returns.

Evidence: pinned Microsoft.AnalysisServices 19.112.0 XML documentation and actual
ConnectionInfo parsing (synthetic localhost string only) expose ConnectTimeout
and Timeout. provider-timeout-properties.json records independently supplied
7 and 11 seconds parsing as 7 and 11. It is API evidence, not a real deadline test.
QueryLifecycleSmoke holds connect/read/cancel until the fixture releases them and
proves the callback/UI remains responsive, ownership remains locked, and no
overlapping run can enter. User cancellation observed at the post-cleanup terminal
commit wins over timeout. Later cancellation cannot rewrite a terminal result.

Missing guarantee: an AMO/authentication/Disconnect/Dispose/CancelCommand call
that fails to return can keep the slot occupied beyond the requested timeout.
No supported in-process proof of a universal hard deadline has been established.
The implementation deliberately retains this active state; it does not abandon
a task, abort a thread or permit a replacement session. OS termination can still
lose an unsaved draft. Provider buffering can allocate before application bounds.

Options for lead after QA/live evidence:
1. Accept an explicitly narrower offline implementation milestone with visible,
   best-effort native timeouts; keep real provider/auth gates open.
2. If a hard deadline is a product requirement, authorize a separately designed
   isolated worker process/protocol and recovery/termination model in a later
   sprint. This is not an authorized S001 transport change.

Decision required: judge these limits against the product claim and assign the
remaining live gate; do not declare full live readiness from controlled fixtures.
No package upgrade/process-isolation workaround was attempted. The assigned
native-timeout/cancellation implementation works with the pinned API; no competing
failed architectural fixes are being concealed.

## Required manual session batch (T10/T11/T12)

Use a disposable synthetic model and record product source/binary hashes and
Desktop/AMO/endpoint versions. No credentials or private query/results in records.

- Native document/keyboard/focus/actual monitor DPI journey; min-size controls and
  dialogs, selected execution, cancel Save As, close/cancel, reopen saved draft.
- Desktop: ROW success, invalid DAX, empty result, row/cell/budget limits, a long
  query with cancel and timeout, disappearing endpoint, close while running, then
  a second successful query. Observe actual duration and UI responsiveness.
- Confirm editing-session continuity and unchanged model fingerprint/dirty state.
- Supported XMLA: repeat relevant cases with valid/expired auth, catalog selection,
  reconnect, timeout/cancel and editor-session continuity. Desktop tests cannot
  establish service-token cloning. The adapter snapshots ConnectionString/catalog;
  externally supplied access-token cloning remains unverified.
- Canonical .NET 10 Core smoke and clean-checkout/remote CI against delivered
  source. No SDK is installed automatically.

## Verification triage

Canonical Core lane: BLOCKED_ENV (.NET 10 absent). Independent net9 diagnostic
project is separately labeled and does not change repository targets.
TabularEditorTest: BLOCKED_ENV, missing legacy VisualStudio/MSTest references.
TOMWrapperTest: builds; chosen 11-case campaign reports 8 passed, 3 failed due to
missing TE_TestServer. Preserve the failed TRX and label those cases BLOCKED_ENV.
Full upstream suite is NOT_RUN.

One intermediate direct host-smoke invocation hit IOException at the existing
DaxDocument atomic replacement (second save). The immediately following isolated
run passed that check, as did subsequent full delivery verification. Cause was not
established; do not call it a proven antivirus issue or silently discard it. The
UI already handles IO failures and preserves draft state. QA should watch for
reproduction; retain as a low-severity validation observation until triaged.

These gates do not block independent offline QA. The light tester should verify
the delivered implementation, then give the lead this decision packet with QA
evidence and any reproducible defects.
