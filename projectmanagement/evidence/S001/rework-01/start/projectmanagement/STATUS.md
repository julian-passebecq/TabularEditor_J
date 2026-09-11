# Current delivery state

Updated: 2026-09-08 by technical lead after independent QA.

| Field | Current value |
|---|---|
| Product | PbiBench: focused TE2-based Power BI engineering workbench |
| Active sprint | [S001 — dependable DAX execution and explicit remote formatting](sprints/SPRINT-001.md) |
| Sprint state | REWORK |
| Next actor | Medium developer |
| Next action | Complete [S001 correction batch 01](sprints/S001-REWORK-01.md): BUG-001 numeric fidelity and BUG-002 async document/context identity, then one light-QA handoff |
| Workspace | D:/PROJ/TabularEditor_J |
| Branch | codex/stabilize-pbibench-v02 |
| HEAD | a7317e7ff1bbdb8949b7e6d82b6277b6e22300de (unchanged; uncommitted delivery) |
| Source identity | [Developer handoff](handoffs/S001-DEVELOPER.md), [final source manifest](evidence/S001/final-source.json), [artifact hashes](evidence/S001/built-artifacts.json) |
| Baseline | Pre-existing stabilization/document/management changes preserved as hashes, patch and complete start-files snapshot |
| Implemented | Ready S001-A/B/C scope: verification runner/CI, query lifecycle/diagnostics/retention, shared formatter consent, robust CSV, document encoding regression, docs/handoff |
| Fresh checks | Actual Release host rebuild and expanded built-host smoke PASS; full runner exit 2 because canonical Core prerequisite is blocked |
| Core canonical check | BLOCKED_ENV: installed SDK 9.0.101 cannot target net10.0; separate net9 diagnostic PASS, not canonical certification |
| Upstream regression | TE2 tests BLOCKED_ENV on legacy references; TOM build PASS, selected campaign 8 PASS / 3 endpoint-dependent failures classified BLOCKED_ENV |
| Live Desktop/XMLA | BLOCKED_ENV: no approved endpoint supplied/exercised; no live readiness claim |
| Native UI journey | PARTIAL: lead visibly opened verified build, loaded synthetic BIM, used Ctrl+P and navigated to Margin; remaining document/keyboard/DPI journey unverified |
| Remote CI | NOT_RUN against delivered dirty source; no push/merge/remote acceptance |
| Lead acceptance | REWORK_REQUIRED. [Lead decision](reviews/S001-DECISION.md); timeout architecture retained with explicit limits, live/auth gates open |

## Immediate instructions

Independent offline QA is complete: [S001-QA](handoffs/S001-QA.md). Its existing
checks passed, but lead logic review found two additional reproducible defects:
float/double CSV values lose precision on net48, and a delayed formatter response
can modify a replacement document with identical text. See [lead decision](reviews/S001-DECISION.md).

Developer: execute [S001-REWORK-01](sprints/S001-REWORK-01.md) as one consolidated
assignment. Preserve current work and historical evidence, fix both boundaries,
check the related expression context, then hand off once to light QA for T14/T15
and affected regressions. Do not start S002 or repeat A/B/C from scratch.

Keep the dedicated AMO session/best-effort timeout architecture. No process or
authentication-stack rewrite is approved. Full runner exit2, canonical net10,
legacy references, remaining native T10, live Desktop/XMLA and remote CI gates
remain open. OBS-001 remains a low-severity unresolved observation.

The user requested returning to architecture/code logic after seeing the app;
routine testing belongs to light QA. The demo app remains open on the synthetic
Margin measure. Lead changed management/evidence only; no production edit,
commit, push, stage, reset or branch switch.
## Progress checkpoints

| Checkpoint | State | Evidence / next step |
|---|---|---|
| Initial lead planning and source review | COMPLETE | [Baseline review](reviews/2026-09-08-baseline.md) |
| S001-A — reproducible baseline and verification | IMPLEMENTED | Preserved baseline; runner/CI and actual rebuild tested. Canonical/remote gates remain open. |
| S001-B — query lifecycle, diagnostics, result bounds | IMPLEMENTED; FOCUSED_TESTS_PASS | Actual executor and Workbench controlled-session tests. Lead/provider questions recorded. |
| S001-C — formatter, export, final delivery | IMPLEMENTED; FOCUSED_TESTS_PASS | Actual proxy/expression/script/CLI, payload, stale/undo and atomic CSV/UI checks; handoff complete. |
| Independent QA | OFFLINE_CAMPAIGN_COMPLETE | S001-QA: exact source verified, fresh host and additional QA tests PASS; missing gates retained |
| Lead logic audit | COMPLETE — REWORK_REQUIRED | BUG-001/BUG-002 and architecture decisions in S001-DECISION |
| S001 correction batch 01 | READY_FOR_DEV | One batch, then targeted light QA and lead correction review |
| Next sprint | CANDIDATE_ONLY | No automatic continuation into new scope |

Implementation, developer verification, QA and lead acceptance are distinct.
No commit, push, merge, clean/reset or branch switch was performed by developer.

