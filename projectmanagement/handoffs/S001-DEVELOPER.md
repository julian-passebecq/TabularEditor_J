# S001 developer handoff

State: READY_FOR_QA (implementation and executable focused checks complete;
canonical Core, live/manual and acceptance gates remain open).
Owner: developer. Updated 2026-09-08. No sprint acceptance granted.

## Short briefing

- Completed ready passes A, B and C without per-pass user prompts.
- Query runs own a dedicated session through cancellation and cleanup; the UI
  stays responsive while cancellation is pending. Safe categories replace raw
  provider messages. Retained results have explicit row/memory/cell/schema limits.
- SQLBI formatting now requires explicit process-session consent centrally,
  including inherited expression and script/CLI paths. Payloads omit all model
  telemetry. Workbench formatting is undoable and discards stale responses.
- CSV writes use stable representations and sibling temporary replacement;
  failures preserve the destination, current results and draft.
- Existing local documents/navigation/history were preserved. One document
  defect was corrected: UTF-16/32 BOM autodetection no longer bypasses UTF-8 rules.
- Next actor: light independent tester. Lead reviews after QA, with the attached
  [provider/live decision packet](S001-LEAD-QUESTIONS.md). No new sprint authorized.

## Source and delivery identity

Workspace: D:/PROJ/TabularEditor_J. Branch: codex/stabilize-pbibench-v02.
Starting/final HEAD: a7317e7ff1bbdb8949b7e6d82b6277b6e22300de.
Upstream at start: origin/pbi-workflow-pro-v0.2 (not freshly fetched).
No commit, staging, reset, branch switch, push or merge was performed.

The delivered source is HEAD plus the current dirty files, including untracked
files; HEAD alone does not identify the build.

- [Starting hashes/ref identity](../evidence/S001/developer-start.json).
- [Preserved starting tracked patch](../evidence/S001/developer-start.patch);
  start-files/ preserves complete starting dirty/untracked contents including
  document, host-smoke and management files.
- [Final active source hashes](../evidence/S001/final-source.json): all active
  dirty/new source and management files. The evidence subtree is excluded from
  this source manifest and inventoried separately to avoid recursive manifests.
- [Built artifact hashes](../evidence/S001/built-artifacts.json): host, copied
  tested host, Core, host harness and TOM artifacts.
- [Final status paths](../evidence/S001/final-git-status.txt) and
  [evidence inventory](../evidence/S001/evidence-manifest.json).
- Compare current files with start-files/ for developer-only changes; compare
  HEAD plus all new files for the full lead acceptance scope.

Pre-existing work retained: host compile/layout fixes, selection/history state,
local DAX documents and their tests, semantic form sizing, Core smoke additions,
integration CI edits and stabilization/management documents.

## Pass checkpoints

| Pass | State | Delivered behavior | Evidence / remaining gate |
|---|---|---|---|
| A | IMPLEMENTED, executable baseline checked | Preserved baseline; deterministic Scripts/Verify-PbiBench.ps1; prerequisite/stage logs; restored environment; both CI lanes cover stabilization branch/build inputs | Baseline actual Release rebuild and host smoke PASS. .NET 10 absent. No clean-checkout remote CI claim. |
| B | IMPLEMENTED, focused checks PASS | Production session seam, request capture, worker cancellation with retained ownership, safe diagnostics, reasons and retained-result limits | QueryLifecycleSmoke drives actual executor and Workbench; native-provider deadline/live questions remain for lead. |
| C | IMPLEMENTED, focused checks PASS | Central formatter consent/no telemetry, inherited and Workbench formatting, atomic CSV/UI errors, UTF-8 document correction, current docs and handoff | Actual proxy/expression/script/CLI and serialized payload tests PASS without network. Native/live acceptance remains separate. |

Final verification rebuilt the product before compiling/executing host smoke.
Independent QA and lead audit have not started.

## Implementation and design notes

### Query lifecycle / terminal state

PbiBenchAmoDaxQueryExecutor snapshots query/max rows/budget/timeout before worker
dispatch; its connection string/catalog were already captured from the loaded
model on the caller thread. Core never references AMO. Only AmoDaxQuerySession
creates/connects/cancels/disconnects/disposes the dedicated Server. The editing
session is not passed to the orchestration.

An interlocked admission flag guards each executor. A token callback only takes a
short state lock and queues one cancellation worker after connection. If a token
fires during Connect, the worker starts once Connect returns. The worker retries
CancelCommand every 100ms after each completed cancellation call while active,
covering the command-start race; there is one worker, not a growing task list.

Finally seals further cancellation scheduling, disposes registration, joins
cancellation, then disposes reader/disconnects/disposes session. Cleanup failures
are suppressed without replacing the outcome. Admission stays occupied through
all these steps. No Task.WhenAny abandonment, thread abort or timeout-as-proof.

The terminal commit is the cancellation sample after cleanup: user cancellation
wins over timeout then; cancellation after that commit leaves the result/history
unchanged. Workbench has one terminal history write and rejects execute/document
replacement/export during the run. Close requests cancellation and keeps the
draft open. See the decision packet for unbounded-provider and OS-shutdown limits.

The pinned AMO ConnectionInfo API parses Connect Timeout and Timeout values
([evidence](../evidence/S001/provider-timeout-properties.json)); requested seconds
are applied to the cloned connection string before Connect. Actual wall-clock
guarantees and authentication cloning need T11/T12.

Diagnostics use phase categories plus explicit ConnectionExceptionCause
AuthenticationFailed/Timeout, UnauthorizedAccessException and TimeoutException.
No provider message, nested exception body, arbitrary code or parsed query fragment
is emitted. Samples: “Could not connect to the query endpoint.” / “The endpoint
could not execute the query.” No error-location parsing is claimed.

### Retention estimate v1

Core owns DaxResultRetention and reason contracts. Defaults: 5,000 rows (maximum
100,000), 32 MiB retained estimate, 256 columns and 1 MiB retained size per cell.
Tests may reduce the budget; no new settings UI was added.

Estimate: 64 bytes/result; 32 bytes/schema array; 8/column reference; string
24 + 2 per UTF-16 code unit; binary 32 + length; 24 per allowlisted boxed scalar;
null/DBNull 0. Each row adds 32 + 8/column plus 32 for list/final references.
Column names count toward the budget. Binary values are copied, accepted values
remain exact, and unknown objects are never stringified.

Field count is checked before allocating rows, and each value is checked before
retention. An offending row is discarded intact; accepted rows remain with an
explicit incomplete reason. Overwide/oversized schema fails safely. Row-cap
lookahead distinguishes exact cap from additional data. One result set only.
The estimate is not exact managed memory, provider-buffer or server-work control.
History has reason and truncation metadata; UI/export status identifies limits.

### Formatter boundary / payload / race handling

All real repository formatter paths converge on DaxFormatterProxy single/multi.
Session/admin checks precede request construction, JSON serialization and redirect
discovery. Recheck consent after redirect before posting. A request already sent
cannot be retracted by revocation. No low-level modal consent prompt.

Actual callers covered: Workbench F6/Ctrl+F6, inherited expression handler,
obsolete script string call, queued object calls/AfterScriptExecution,
enumerable batch, and fresh CLI script single/batch invocation. Trusted arbitrary
C# can independently use networking; this policy governs repository formatting.

The process defaults disabled; Workbench Remote formatting enables/revokes.
Administrative disable overrides consent. Both DTO constructors no longer call
ModelTelemetry.Collect; their request type no longer inherits model/server fields.
The independent Core telemetry policy semantics remain compatible.

Serialized allowlist, single and multi: Dax; MaxLineLenght (upstream spelling);
SkipSpaceAfterFunctionName; ListSeparator; DecimalSeparator; CallerApp;
CallerVersion. HTTP also carries ordinary transport headers. This is not “DAX only.”

Existing long/short/separator and UDF wrapping/comment extraction remain.
Workbench formats the whole draft off-thread, replaces through SelectedText for
one undo, and rejects response after any text revision, cancel, close or consent
change. Pending format keeps new formatting/query admission closed until return.
Inherited expression handler also runs off-thread, blocks overlap and rejects
any text-change/object/consent mismatch. Failures do not apply provider responses.

### Export and documents

DaxCsvExport writes quoted comma-delimited fields with doubled quotes and CRLF row
endings, UTF-8 BOM, invariant scalars, DateTime/DateTimeOffset O format, TimeSpan c,
Base64 binary and null-as-empty. Embedded text/newlines and formula-like text are
preserved; no spreadsheet formula-safety transformation.

Save writes a uniquely named sibling temporary file, closes it, then uses
File.Replace or File.Move. Finally cleans temporary files best effort. This is
ordinary atomic replacement, not crash durability or a concurrent-editor lock.
Workbench injectable dialog/save delegates exercise cancel/write-error handlers;
a safe status preserves draft/result/export eligibility after failure.

DaxDocument still uses explicit saves, bounded 4 MiB input/output and external
content-hash checks. UTF-8 decoding is strict (optional UTF-8 BOM); UTF-16/32 is
rejected rather than autodetected. Concurrent external write after hash check
remains a documented race; no autosave or recovery was introduced.

### Ownership / dependencies

Neutral contracts/estimate/CSV/document rules remain in PbiBench.Core.
AMO/WinForms/HTTP remain in host adapters. Three upstream source edits are listed
in [UPSTREAM_TE2_SYNC](../../docs/pbibench/UPSTREAM_TE2_SYNC.md).
No production dependency pins or target frameworks changed. Host smoke references
the existing pinned Newtonsoft assembly and accesses internals through an additive
friend-assembly attribute. The net9 diagnostic project is isolated under evidence;
it never overrides or retargets the canonical net10 project.

## Developer verification

Environment: Windows; VS Build Tools 17.12.35527.113; .NET Framework 4.8 refs;
installed stable SDK 9.0.101 (also an older 9 preview); pinned AMO 19.112.0.
NuGet resolved explicitly from D:/PROJ/powerbi_enhanced_dev/.tools/nuget.exe.
No SDK was installed and no package pins were upgraded. Resolver/PATH adjustments in the
runner are process-local and restored in finally.

| Test ID | Command / exact scope | Result | Evidence / limits |
|---|---|---|---|
| T01/T03 | powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Verify-PbiBench.ps1 -NuGetPath D:/PROJ/powerbi_enhanced_dev/.tools/nuget.exe -EvidenceDirectory D:/PROJ/TabularEditor_J/projectmanagement/evidence/S001/delivery-build | Full command exit 2: INCOMPLETE | [stage summary](../evidence/S001/delivery-build/summary.json). Legacy restore, Core restore, ANTLR Debug rebuild, host Release rebuild, host-smoke build/run each exit 0. Required Core lane BLOCKED_ENV, not skipped-as-PASS. |
| T02 | dotnet run --project PbiBench.Core.Smoke/PbiBench.Core.Smoke.csproj -c Release | BLOCKED_ENV, exit 1 / NETSDK1045 | [canonical log](../evidence/S001/core-canonical.log); .NET 10 absent. |
| T02 diagnostic only | dotnet run --project projectmanagement/evidence/S001/core-net9-diagnostic.csproj -c Release | PASS, exit 0 | [diagnostic log](../evidence/S001/core-net9-diagnostic-final.log). Same canonical smoke sources plus new neutral checks, referencing final built Core; different runtime, not canonical certification. |
| T04–T09 focused | Fresh built PbiBench.Host.Smoke/bin/Release/net48/PbiBench.Host.Smoke.exe (runner) | PASS, exit 0 | [host log](../evidence/S001/delivery-build/host-smoke-run.log). Existing navigation/document/history plus controlled executor, production close, deadline races, secret sentinels, boundary results, actual formatter callers/payloads, stale/undo/error and CSV data/files/UI. |
| T03 upstream TE2 | VS MSBuild TabularEditorTest/TabularEditorTest.csproj /m /p:Configuration=Release /p:Platform=AnyCPU /verbosity:minimal | BLOCKED_ENV, build exit 1 | [log](../evidence/S001/upstream-te2-build.log). Missing legacy VisualStudio/MSTest references. No suite migration. |
| T03 upstream TOM | Same MSBuild flags on TOMWrapperTest/TOMWrapperTest.csproj; vstest.console.exe TOMWrapperTest/bin/Release/TOMWrapperTest.dll /TestCaseFilter:FullyQualifiedName~UndoManagerTests|FullyQualifiedName~ObjectHandlingTests /Logger:trx;LogFileName=S001-tom.trx /ResultsDirectory:projectmanagement/evidence/S001/tom-results | Build PASS; campaign exit 1: 8 passed, 3 blocked by missing endpoint | [test log](../evidence/S001/upstream-tom-tests.log), [TRX](../evidence/S001/tom-results/S001-tom.trx). Raw runner reports 3 failed; ResetTest, DeleteTableTest, BatchActionTest require TE_TestServer. Full suite NOT_RUN. |
| T03 warnings | Normalize only line/column numbers and compare baseline vs final host warning sets | No introduced warning entries | [comparison](../evidence/S001/warning-comparison.json). Existing reference/ANTLR/native-interop/compiler warnings remain. Host smoke builds with zero warnings/errors. |
| T10 | Native dialog/keyboard/focus/actual monitor DPI journey | NOT_RUN | Handle/layout/scaled automated checks do not replace this. Native automation is unavailable in this task. |
| T11/T12 | Real Desktop and supported XMLA/auth | BLOCKED_ENV | No approved live endpoint supplied/exercised. Use the decision packet's single session checklist. |
| T13 | Full active product diff/source review and git diff --check | PASS (developer review only) | Scope/ownership, cleanup, safe diagnostics, shared formatter gate, retained estimates and file boundaries inspected. Not independent QA or lead acceptance. |
| Remote CI | Exact dirty delivered source / clean-checkout remote run | NOT_RUN | No push/merge/remote certification. Workflow trigger changes reviewed locally. |

First-failure evidence is retained in implementation-build/, focused-build/,
expanded-build*/ and expanded-smoke-retry.log. Compile/harness corrections included
the AMO Action ambiguity, typed FCTB text event, pinned Newtonsoft reference and
valid synthetic model serialization options. They are resolved in final checks.
One transient pre-existing document replacement IOException remains a QA
observation in the lead packet; no silent retries or test weakening were added.

## Risks / escalation / handoff

R-001 / DAX-001: native deadline/authentication guarantees and live continuity
remain open, owned by lead after QA. No full live-readiness claim.
FND-001: canonical Core/remote CI environment evidence still required.
DAX-005/DAX-006 validation observation: watch for the isolated document replacement
IOException; failure UI preserves state, root cause not established.
T10 native interaction remains required. No known untriaged high-severity product
failure was observed in final executable tests.

Light tester should now independently rebuild the exact manifest, run TEST-PLAN,
maintain BACKLOG/REGISTERS and finish S001-QA. Reproduce defects without silently
fixing production code. Return ordinary defects as a consolidated rework batch;
otherwise bring the lead the QA report and S001-LEAD-QUESTIONS. The lead owns
acceptance and any next-sprint/process-isolation decision.

Tester prompt:

> Act as the independent tester and delivery-record maintainer. Read AGENTS.md,
> projectmanagement/STATUS.md, the active sprint, TEST-PLAN.md, and
> projectmanagement/handoffs/S001-DEVELOPER.md. Verify the exact delivered source,
> rebuild and run the required checks, reproduce failures, maintain BACKLOG.md and
> REGISTERS.md, and complete S001-QA.md. Tell me whether to return to the developer,
> call the technical lead, or provide a missing test environment. Do not silently
> fix production code or approve the sprint.

## S001-REWORK-01 developer correction handoff — 2026-09-08

State: READY_FOR_QA for the consolidated correction batch. BUG-001/BUG-002 are
implemented and developer-verified, not independently verified or accepted.
The original S001 delivery above and the prior QA report remain historical evidence.
Lead decision S001-DECISION retains the current query architecture with explicit
timeout/auth limits; no transport or authentication rewrite was made.

### Source preservation and scope

Branch/HEAD unchanged: codex/stabilize-pbibench-v02,
a7317e7ff1bbdb8949b7e6d82b6277b6e22300de. No commit/staging/push/reset/branch switch.
The running user app was not interacted with, closed or restarted.

[evidence/S001/rework-01/start-source.json](../evidence/S001/rework-01/start-source.json)
records the pre-correction dirty source; start/ preserves each complete active
dirty/untracked file. Earlier root final-source.json and built-artifacts.json were
preserved as prior-final-source.json and prior-built-artifacts.json in rework-01/
before refreshing the latest delivery manifests. Previous QA/lead evidence and
reports were not overwritten. Root final-source.json and built-artifacts.json now
describe this corrected delivery; correction-specific copies are also in rework-01/.

Production changes:
- PbiBench.Core/Dax/DaxResultRetention.cs: explicit invariant Double G17 / Single
  G9 before generic conversion; both zero signs explicitly render 0.
- Workbench Documents/Formatting partials: successful replacement advances a
  document generation in DisplayDocument (called only by New, successful Open and
  history recall). Format admission captures it along with text/consent revision.
  Failed/cancelled operations never reach that boundary. Save/Save As does not.
- UIController.cs / UIController_ExpressionEditor.cs: read-only expression context
  generation changes on Handler assignment, current-object assignment and the
  existing selected-DAX-property event. Property changes performed during Preview
  while the selector handler is detached are covered by the current-object
  assignment at the end of Preview. This also invalidates away/back transitions.
- FormMain.cs captures controller identity and context generation before dispatch
  and checks both before application, retaining text/object/consent/disposal guards.
  This closes the related equal-text property/model context gap found in audit.
- Added Rework01Smoke.cs to the existing built-host smoke entrypoint; README and
  upstream ledger document numeric policy and the minimal upstream edits.

Typed query retention, AMO transport, formatter consent and model editing semantics
are unchanged. Finite floating-point values round-trip numerically. NaN, Infinity
and -Infinity retain invariant textual spelling; negative zero becomes canonical
0. This is not a bit-preserving IEEE serialization. Decimal/date/binary/text/null
conversion remains on its prior path.

### Developer verification and evidence

Host source was rebuilt by:
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Verify-PbiBench.ps1
-HostOnlyDiagnostic -NuGetPath D:/PROJ/powerbi_enhanced_dev/.tools/nuget.exe
-EvidenceDirectory D:/PROJ/TabularEditor_J/projectmanagement/evidence/S001/rework-01/build

Restore/ANTLR/product rebuild each exited 0. That first invocation ended at a
test compile error (missing DAXProperty namespace), preserved in build/. Production
source did not change after that successful rebuild. Harness-only corrections
then fixed the namespace, constructed TOM fixtures in singleton-compatible order
and called LoadTabularModelToUI to initialize TE2 navigation state. First-failure
logs remain; none was a hidden passing claim or a production workaround.

Final harness command:
dotnet build PbiBench.Host.Smoke/PbiBench.Host.Smoke.csproj -c Release --nologo
PASS, exit 0, zero warnings/errors ([log](../evidence/S001/rework-01/harness-build-3.log)).

Final actual built-host invocation:
PbiBench.Host.Smoke/bin/Release/net48/PbiBench.Host.Smoke.exe
PASS, exit 0 ([log](../evidence/S001/rework-01/focused-run-3.log)).
This ran the existing standard host checks plus Rework01Smoke against the rebuilt
host. No external SQLBI call; recording transport only.

Focused corrected behavior:
- T14: reported Double 1.0000000000000002 and Single 1.00000012, negative value,
  subnormals, maxima, NaN/infinities and explicit IEEE negative-zero bit patterns.
  Actual CSV fields parsed under invariant culture compare to independently typed
  values under en-US/fr-CH. Actual grid CellValueNeeded agrees; fixed spelling and
  canonical-zero assertions prevent an oracle based solely on the same helper.
- T15 Workbench: real child handles; held formatting across equal-text Open to B,
  same-path reopen, empty New and equal-text history recall. Text/path/dirty/undo
  remain unchanged after stale release. Failed/cancelled Open retains generation
  and valid same-buffer formatting still applies. Save/Save As keeps generation,
  then valid formatting makes the buffer dirty and remains one-edit undoable.
- T15 expression: model-bound real UI controller and formatter handler; equal-text
  object away/back, selected DAX-property away/back, and handler away/back reject
  pending results. Unchanged same-context formatting applies normally.
- Existing T07/T08/T09/document/history/undo and broader standard host smoke PASS.
  Light QA owns the full independent edge-case matrix; these are developer tests.

The existing missing .NET 10 SDK, legacy test-reference, remote CI, live
Desktop/XMLA/auth, remaining native T10 and OBS-001 gates remain open. They were
not re-run or waived in this focused correction. The lead's native Quick Open
evidence remains valid only for its original described scope.

### Next actor

Start the light tester once for S001-REWORK-01. Rebuild/identify the corrected
source; independently execute T14/T15 and affected T07/T08/T09/document regressions;
write S001-QA-REWORK-01.md and update BACKLOG/REGISTERS/STATUS. Do not overwrite
S001-QA.md or mark the sprint accepted. If green, return to lead for focused
correction review and the next-sprint decision. No S002 work was started.
