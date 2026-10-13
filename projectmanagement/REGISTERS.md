# Branch, environment and verification register

Maintainer: light tester. Initial observation: 2026-09-08, technical lead.
These are local observations, not a live remote synchronization report.

## Branches and worktrees

| Branch/ref | Locally observed head | Role / state | Next action |
|---|---|---|---|
| `codex/stabilize-pbibench-v02` | `a7317e7` plus dirty stabilization work | Active local delivery; tracks `origin/pbi-workflow-pro-v0.2` | S001; preserve/include pending changes |
| `master` / `origin/master` | `7029129` | Clean upstream baseline by branch intent; not checked out | Keep product work off master |
| `origin/pbi-workflow-pro-v0.1` | `b717ba8` | Foundation checkpoint; remote-tracking ref | Reference only; do not confuse with release acceptance |
| `origin/pbi-workflow-pro-v0.2` | `a7317e7` | Product integration remote-tracking head | Future integration after review and authorized publication |
| `upstream/master` | `7029129` | Pinned TE2 baseline ref; remote-tracking | Sync only in separately scoped maintenance work |
| Other `upstream/*` feature refs | See baseline snapshot JSON | Upstream reference branches, not PbiBench delivery candidates | No tests implied; do not merge speculatively |
| `D:\PROJ\TabularEditor_J` | Active branch above | Primary working tree | Development and QA source of truth |
| `D:\PROJ\TabularEditor_J-audit-v02` | `a7317e7` detached | Historical audit worktree | Preserve as evidence; never confuse its executable with current delivery |

Initial ref/dirty-file inventory: [baseline-source.json](reviews/baseline-source.json).
If branches move, append/update observed date and source identity. Do not infer
remote freshness from a remote-tracking ref. When remote CI is checked, record
run URL, exact commit, event, outcome and whether it included all delivered files.

## Pre-existing pending work at management setup

| Paths | Description / ownership at baseline |
|---|---|
| `.github/workflows/pbibench-te2-integration.yml` | Expanded trigger paths and host-smoke steps |
| `PbiBench.Core.Smoke/Program.cs`, `PbiBench.Core/Dax/DaxQueryContracts.cs` | History state/row/truncation metadata and checks |
| `TabularEditor/PbiBench/Dax/PbiBenchAmoDaxQueryExecutor.cs` | AMO Server alias compile fix |
| `TabularEditor/PbiBench/Dax/PbiBenchDaxWorkbenchForm.cs` | Layout, selection/history/error/document integration |
| `TabularEditor/PbiBench/Semantic/PbiBenchSemanticViewForm.cs` | Split-container construction fix |
| `PbiBench.Core/Dax/DaxDocument.cs` (untracked) | Local document rules and file I/O |
| `TabularEditor/PbiBench/Dax/PbiBenchDaxWorkbenchForm.Documents.cs` (untracked) | File commands and dirty/replacement guards |
| `PbiBench.Host.Smoke/*` source/project files (untracked) | Built-host dialog/workflow/document checks |
| `docs/PBIBENCH_AUDIT_2026-09-08.md`, `docs/pbibench/STABILIZATION_2026-09-08.md` (untracked) | Previous audit/stabilization records |

The management setup adds root `AGENTS.md`, a README routing link and this folder.
It does not commit, push, merge or change production source. Use hashes/source
snapshot to identify the dirty baseline; a `git diff --stat` alone omits untracked
feature files and is not a complete acceptance scope.

## Environments and test results

| Lane | Baseline evidence | State | Owner / next step |
|---|---|---|---|
| Windows host build | VS2022 Build Tools, SDK9 resolver, Release; current host compiled | PASS with assembly-reference warnings | QA fresh rebuild PASS; warning comparison unchanged; see S001-QA |
| Built-host smoke net48 | Fresh harness build and execution; layout/query/document suites | PASS | QA delivered smoke and added adversarial checks PASS; native T10 NOT_RUN |
| Core canonical net10 | Standard run attempted; NETSDK1045 with SDK9.0.101 | BLOCKED_ENV | QA confirmed NETSDK1045; supported SDK/CI needed |
| Prior alternate Core net9 checks | QA isolated net9 diagnostic exit0 | PASS diagnostic only | Do not count as net10 evidence |
| `TabularEditorTest` legacy suite | Prior audit says unavailable legacy VS test framework; csproj still references it | BLOCKED_ENV, QA build exit1 | Dev/QA identify exact relevant checks and infrastructure issue |
| `TOMWrapperTest` | QA build PASS; selected 11-case campaign | 8 PASS / 3 BLOCKED_ENV | Missing TE_TestServer; raw runner exit1; full suite NOT_RUN |
| Remote CI final changes | No remote job inspected/executed in this turn | NOT_CHECKED | Record run/commit after authorized publication |
| Native dialogs/real DPI | Existing smoke uses controlled dialog replies and simulated scaling | NOT_RUN | QA T04/T10 native session |
| Live Desktop | No actual endpoint exercised in this turn | NOT_RUN | QA T11; establish available fixture |
| Live service XMLA/auth | No actual endpoint exercised in this turn | NOT_RUN | QA T12; establish endpoint/auth class |

Fresh validation details and limitations: [baseline review](reviews/2026-09-08-baseline.md).
The tester adds final sprint test evidence in S001-QA, then links it here; do not
copy long logs into multiple files.

## Decision queue

| ID | Question | Owner / due |
|---|---|---|
| R-001 | Lead retains in-process owned sessions and best-effort timeouts; actual live deadline/auth behavior remains unverified | Design decision in S001-DECISION; QA T11/T12 still required |
| R-002 | Which live Desktop/XMLA and real-DPI cases are available for acceptance? | QA preparation; user supplies unavailable environment only |
| R-003 | How will final net10 and relevant legacy test evidence be obtained on this workstation/CI? | Dev S001-A; QA verifies actual outcomes |

## Pass sizing feedback

| Sprint | Developer continuations requested from user | Approximate active pass sizes, if known | QA/rework cycles | User feedback / next adjustment |
|---|---|---|---|---|
| S001 | Developer reports no per-pass user prompts | A–C completed; active durations not measured | One independent QA campaign; lead found two further defects, one correction batch assigned | Preserve substantial passes; routine testing goes to light QA; next actor medium developer |

## S001 QA final observation — 2026-09-08

Authoritative fresh evidence: [S001-QA](handoffs/S001-QA.md).
Branch/HEAD unchanged: codex/stabilize-pbibench-v02 / a7317e7ff1bbdb8949b7e6d82b6277b6e22300de.
All 47 developer hashes matched; no production/delivered test changes by QA.
Management records and isolated qa/ evidence/tests only were added/updated.
Nothing staged, committed, pushed, reset or switched. Remote refs not refreshed.

Full runner child exit2 confirmed via Process.ExitCode; host build/smoke PASS twice.
Net10 blocked; separate net9 diagnostic PASS. Legacy TE2 build blocked by MSTest
references; TOM selected suite raw 8 passed / 3 failed, classified endpoint-blocked.
Host warning comparison has zero differences from developer delivery.
Native T10 NOT_RUN; Desktop/XMLA approved fixture absent (T11/T12 BLOCKED_ENV).
Four msmdsrv processes observed, no PBIDesktop process; unknown engines not queried.
Remote CI NOT_RUN against dirty source. No acceptance granted.

QA adversarial checks: held Dispose with cancellation/overlap denial and subsequent
session isolation; provider binary-copy isolation; 50 exact document replacements.
All PASS. OBS-001 low-severity prior IOException remains open, tester owner,
non-reproduced; capture recurrence before assigning product/environment cause.
R-001 belongs to lead for deadline/auth decision; R-002/R-003 need environments.
Next actor technical lead; no ordinary developer rework batch found.

## Lead decision following that QA campaign — 2026-09-08

[S001-DECISION](reviews/S001-DECISION.md): REWORK_REQUIRED for BUG-001 numeric
precision and BUG-002 async document identity. Both are reproduced against the
delivered assemblies; see lead/probe-run.log. The isolated probe build passed;
run exit1 reflects five detected violations belonging to two defects. Production
source/tests remain unchanged by lead; review fixtures and management records added.

At intake, 43/47 developer-manifest paths matched; the other four were exactly
QA's management updates. The displayed repository executable matched QA's host
hash. Do not confuse it with the installed Tabular Editor 2.24.1: the repository
build is Tabular Editor 2.28.0 at TabularEditor/bin/Release/TabularEditor.exe.

Native subset observed: opened synthetic PbiBench-Demo.bim, Ctrl+P opened Quick
Open, double-click Margin navigated/displayed the expected expression. No dirty
title marker, model save, live endpoint or remote formatting exercised. Full T10
and DPI remain unverified. User requested stopping the UI work and focusing lead
time on code logic; app left open, routine testing assigned to light QA.

Next actor medium developer: sprints/S001-REWORK-01.md. Then light QA records
S001-QA-REWORK-01, then lead correction review/next-sprint decision. No full S001
acceptance and no new feature sprint authorized. R-001 design is decided with
limits; live/auth/canonical/legacy/remote verification remains open.

## Correction QA — 2026-09-08

[S001-QA-REWORK-01](handoffs/S001-QA-REWORK-01.md): all 51 source entries matched, branch/HEAD unchanged. HostOnlyDiagnostic rebuild and standard host smoke stages exit0; isolated extended QA final build/run exit0. BUG-001/BUG-002 QA_VERIFIED, lead next. Prior evidence preserved; only new QA fixtures/evidence and management updates. No production edit, staging, commit, push or app interaction. Wider canonical/upstream/live/native/CI gates unchanged and not rerun. One QA fixture setup error preserved and corrected; no product rework cycle added.

