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
| Windows host build | VS2022 Build Tools, SDK9 resolver, Release; current host compiled | PASS with assembly-reference warnings | Dev keeps warnings visible; QA rebuilds final source |
| Built-host smoke net48 | Fresh harness build and execution; layout/query/document suites | PASS | QA expands T04–T10 for final source |
| Core canonical net10 | Standard run attempted; NETSDK1045 with SDK9.0.101 | BLOCKED_ENV | Dev S001-A; supported SDK/CI lane needed |
| Prior alternate Core net9 checks | Earlier stabilization doc says passed | REPORTED_PRIOR | Do not count as fresh net10 evidence |
| `TabularEditorTest` legacy suite | Prior audit says unavailable legacy VS test framework; csproj still references it | REPORTED_PRIOR block; not rerun here | Dev/QA identify exact relevant checks and infrastructure issue |
| `TOMWrapperTest` | Project inspected for setup only | NOT_RUN | QA assesses independently; no inference from other suite |
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
| R-001 | Can the pinned AMO provider support the required connection/command lifecycle without abandoning active sessions? | Dev evidence in S001-B; lead if a boundary change is needed |
| R-002 | Which live Desktop/XMLA and real-DPI cases are available for acceptance? | QA preparation; user supplies unavailable environment only |
| R-003 | How will final net10 and relevant legacy test evidence be obtained on this workstation/CI? | Dev S001-A; QA verifies actual outcomes |

## Pass sizing feedback

| Sprint | Developer continuations requested from user | Approximate active pass sizes, if known | QA/rework cycles | User feedback / next adjustment |
|---|---|---|---|---|
| S001 | Not started | Three substantial passes planned; no artificial duration minimum | Not started | User prefers long autonomous passes; reassess after actual delivery |
