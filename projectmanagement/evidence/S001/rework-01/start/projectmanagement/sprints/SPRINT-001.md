# S001 — Dependable DAX execution and explicit remote formatting

State: REWORK after initial development and independent QA. Owner: medium developer. Independent QA: light tester.
Acceptance: technical lead. Baseline: STATUS and baseline review.

Current assignment: [S001-REWORK-01](S001-REWORK-01.md). The original A/B/C packet
below defines the delivered scope and historical acceptance requirements; do not
restart those passes. The lead found BUG-001/BUG-002 and has not accepted S001.

## Outcome and scope

A user can preserve a DAX draft, execute a bounded query on a dedicated session,
request cancellation without freezing the UI, understand failures/truncation,
export safely, and choose whether formatting sends DAX off-device. The team can
rebuild and verify the delivered implementation and distinguish automated evidence
from actual Desktop/XMLA acceptance.

Include the existing uncommitted compile/layout, history/selection and document
changes in acceptance. Reuse them. This sprint does not include a rich editor,
multiple result sets, autosave, scripts, project UI, report editing or a shell
redesign. Keep one-result-set behavior explicit until its follow-up backlog item.

Three passes are authorized as one development assignment. Complete each pass's
checks, checkpoint, and continue. If a hard dependency blocks one item, document
the exact issue and advance independent authorized work. Do not silently remove
blocked acceptance requirements.

## Pass A — Establish a reproducible delivery and regression baseline

Backlog: FND-001, FND-002. Milestone: a known source snapshot and a repeatable
verification entry point, or explicit environment blocks with the rest runnable.

1. Record the initial branch/HEAD/dirty paths and file hashes. Review pending
   stabilization sources, including untracked document and host-smoke files.
   Preserve their changes; do not reset to the old product commit.
2. Rebuild the actual TE2 host and host smoke; run Core using the supported target
   when available. Capture warnings and distinguish old baseline warnings from
   introduced warnings. SDK 9 alone cannot certify the net10 harness.
3. Add `scripts/Verify-PbiBench.ps1` as a deterministic verification entry point.
   It should resolve tools, check SDK/reference prerequisites, restore required
   packages, generate ANTLR Debug sources, build Release host, build/run Core smoke
   and built-host smoke, with each stage's exit code and concise summary. Preserve
   process environment after temporary MSBuild resolver adjustments. Do not install
   SDKs automatically, change framework targets, or treat a skipped stage as PASS.
   Support a clearly named host-only diagnostic mode if needed locally; the full
   default command fails/incompletes when a required prerequisite is missing.
4. Align CI paths/branches for both lanes with actual product, smoke, build
   infrastructure and package inputs. The current foundation push filter omits
   the stabilization branch. Include the new verification script where relevant.
   Keep clean-checkout restore/build order correct. Do not weaken net10 CI.
5. Inventory relevant upstream regressions. The legacy `TabularEditorTest` assembly
   uses unavailable legacy framework references locally; `TOMWrapperTest` has its
   own configuration and needs separate assessment. Run feasible relevant checks,
   document blockers, and add direct behavioral host tests for formatter paths
   changed in this sprint. Do not migrate the entire upstream test suite here.

Checkpoint A records the exact commands/environment, source snapshot, existing
passes/failures and available test seams. Do not stop merely because a baseline
issue is one of the already assigned fixes in B or C.

## Pass B — Query lifecycle, safe diagnostics, bounded retained results

Backlog: DAX-001, DAX-002, DAX-003. Milestone: the lifecycle works through the
production orchestration with controlled transports and is ready for live checks.

Primary files: `PbiBench.Core/Dax/DaxQueryContracts.cs`,
`TabularEditor/PbiBench/Dax/PbiBenchAmoDaxQueryExecutor.cs`,
`TabularEditor/PbiBench/Dax/PbiBenchDaxWorkbenchForm.cs`, and their tests.

### Execution ownership and cancellation

- Retain the pinned AMO package unless evidence requires a lead decision. Extract
  a small internal session/factory seam so tests can control connect/read/cancel/
  disconnect without replacing the executor with an unrelated fake. Core must
  not reference AMO. Exercise the real orchestration around the fake session.
- Capture request values and source connection identity for the run. Do not read
  mutable UI/model state on worker threads. Use only a dedicated query session;
  the TE2 editing session must remain untouched through all exits.
- Give exactly one owner to session/reader/registration cleanup. A token callback
  must not invoke synchronous `CancelCommand` on the UI thread. Dispatch the
  cancellation work, coordinate it with connect and disposal, and keep the UI
  responsive without losing ownership of unfinished work.
- Test cancellation before connecting, while connecting, before reader creation,
  during blocking read, after completion, and during close. Multiple cancel/close
  requests must be harmless. Cleanup exceptions must not replace the original
  outcome. A delayed cancel must never hit a later run or a disposed session.
- Verify the pinned provider's connection and command timeout facilities from
  its actual API/docs and record the chosen mechanism. A `Task.WhenAny` timeout
  alone is not cancellation. Do not report a hard deadline or silently permit new
  runs while abandoned calls remain active. If an in-process call cannot be
  bounded, keep that state visible and escalate the missing guarantee; continue
  other ready work. An isolated process is a lead decision, not an ad hoc patch.
- Preserve Completed/Failed/Cancelled compatibility. Add a neutral reason/code
  if needed to distinguish user cancellation, timeout and result limits. Define
  the outcome race: user cancellation requested before the terminal result is
  committed wins over a simultaneous timeout; cancellation after terminal
  completion does not rewrite history. Only one terminal history entry per run.
- While a run/cleanup remains active, prevent overlapping execution and document
  replacement/export. Preserve the current close-cancels-and-keeps-draft behavior.
  Record any remaining operating-system shutdown limitation; do not invent
  crash recovery in this sprint.

### Diagnostics

- Replace raw `ex.Message` propagation with safe, useful categories (connection/
  authentication/query/timeout/cancel/unexpected) and allowlisted location/error
  codes when the provider exposes trustworthy structured fields. Do not rely on
  trimming, truncating, or a few regular expressions as the privacy boundary.
- Query text, connection strings, tokens and provider exception bodies must not
  enter generic status/history/log output. Preserve the user's own draft and
  intentional memory history; those are not diagnostic channels. Use synthetic
  sentinel secrets in adversarial tests, never real credentials.
- UI remains usable after all errors. A later failure/cancel clears earlier rows;
  export cannot accidentally use a previous successful result.

### Result retention

- Keep current row bounds (default 5,000, contract maximum 100,000). Add a default
  retained-result estimate budget of 32 MiB, maximum 256 columns and a 1 MiB
  retained size limit per string/binary cell. These are initial application
  limits; name and document the estimate algorithm, including strings, arrays,
  row/column overhead and primitive values. No settings UI is required now.
- Do not accumulate an oversized row before checking the known field count and
  each fetched value. Stop retaining data when the budget is reached. Return
  intact accepted rows and an explicit limit reason; never silently clip cell
  text and label it an exact query result. An over-wide schema may return a safe
  failure with the column-limit reason. Do not call `ToString()` on arbitrary
  provider objects to estimate size or render unknown values.
- Test below/exactly/above the row cap and budget, oversized first cell, many
  columns, nulls and wide Unicode values. Completed limited results are visibly
  incomplete in grid/history/export status. A result limit does not rewrite DAX
  or claim to bound server execution; provider buffering is a documented limit.

Checkpoint B includes a lifetime/state description, targeted race-test evidence,
safe-diagnostic samples and unresolved live-provider questions. If those questions
require the lead, write the escalation and proceed with independent Pass C work.

## Pass C — Enforce formatter consent, robust export, deliver the milestone

Backlog: DAX-004, DAX-005, DAX-006, FND-003. Milestone: coherent user flow, no known
policy bypass in repository formatter paths, and a complete QA-ready handoff.

### Formatter boundary and UX

- Apply ADR-005 to the real service paths. Trace Workbench formatting, inherited
  expression F6/Ctrl+F6, script single/batch and CLI formatting through their
  actual proxy calls. Enforce the administrative disable and PbiBench consent
  check centrally before `PrimeConnection`, payload creation or any network call.
- Use explicit session-only consent (disabled on each process start), with an
  enable/revoke action reachable from PbiBench UI. Explain that SQLBI remote
  formatting sends DAX text and that this session consent also governs inherited
  formatter calls. A script or CLI request while disabled fails clearly without
  a modal prompt or implicit enablement. The GUI directs the user to the consent
  control; do not open consent prompts from low-level service code.
- S001 does not collect/send model telemetry even if the inherited preference is
  true. Keep the neutral policy's independent telemetry semantics compatible,
  but do not add a telemetry settings feature. Build a payload from allowed DAX
  and formatting options plus necessary, documented app/protocol fields. Test
  serialized payloads, including single and multi calls. Inventory every field
  actually sent; do not call it "DAX only" if app/version fields also travel.
- Add Workbench Format action with clear remote provenance. Retain default query
  execution shortcuts; F6 can format the entire current draft, explicitly labeled.
  Disable formatting during queries/another format request. Apply a successful
  result as one undoable edit only if the document revision still matches; on
  close, failure, cancellation or stale response preserve the current draft.
- Preserve long/short formatting, separators and existing UDF wrapping/comment
  extraction behavior. Keep requests off the UI thread and avoid modifying global
  telemetry settings temporarily to suppress payload fields. No new formatter
  client package or offline formatter is required.
- Test with a recording transport/proxy that proves a denied request makes zero
  calls, including redirect discovery. Do not call the external service with real
  model text as an automated test. A live service check, if performed, uses only
  a synthetic DAX sample and explicit application consent.
- Upstream edits to the formatter boundary/callers are authorized where necessary;
  keep them small and record them in the sync ledger with regression evidence.

### CSV and existing document acceptance

- Extract a testable export operation and injectable dialog/file failure path.
  Handle file-access/encoding/write failures and report a safe error without
  throwing out of a WinForms event handler or losing results/draft state.
- Write a sibling temporary file and replace/move only after success. Preserve an
  existing destination on failures, respect overwrite-dialog cancellation, and
  clean up temporary files as best effort. This is atomic replacement, not a
  guarantee of power-loss durability or locking out every concurrent editor.
- Define stable CSV output: comma delimiter, all fields quoted, quotes doubled,
  embedded newlines preserved, UTF-8 BOM, invariant numeric conversion and an
  explicit round-trip timestamp convention for known date types. Null becomes
  empty. Preserve text contents; this is data export, not an Excel formula-safety
  transformation. Document that distinction if exposing spreadsheet-specific UX
  later. Do not silently trim formula-like text or change numeric values.
- Export only the currently displayed completed result and show when it is
  limited. Test commas/quotes/newlines/Unicode, null/date/decimal values under at
  least two cultures, cancelled dialog, locked/read-only destination and partial
  write failure. Test the actual UI error path, not only a string helper.
- Re-run existing file/dirty/history protections with current code. Preserve
  bounded UTF-8 document handling and documented concurrent-editor limitations.
  New document recovery/multi-tab work stays in the backlog.

### Finalize

Re-run affected checks on the final source. Reconcile README/roadmap/v0.2 feature
descriptions with verified behavior and explicit limitations; retain historical
audit attribution. Complete `handoffs/S001-DEVELOPER.md`, update STATUS and the
upstream ledger. The handoff must identify all changes and known blocks without
requiring QA to reconstruct the entire coding conversation.

## Exit gate and handoff

- A/B/C implementation and focused regression checks complete, or each incomplete
  item explicitly assigned and escalated. Do not label incomplete scope done.
- Fresh host build, Core checks, actual built-host tests and controlled-session/
  formatter/export checks recorded with exact source identity.
- Required independent QA includes live Desktop/XMLA where available. Missing
  environments stay BLOCKED_ENV and prevent full live acceptance.
- No untriaged high-severity issue; no hidden formatter bypass; no claims that a
  token, row cap, fake executor, or document hash provides stronger guarantees
  than it does.
- Developer ends with READY_FOR_QA and the tester prompt. QA produces its packet.
  The lead audits the complete acceptance scope and decides the gate.

Provisional next outcome: model-aware DAX editor foundation. Do not start it
automatically or import the old studio while waiting for S001 review.
