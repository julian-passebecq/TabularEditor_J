# Pro AI takeover — PbiBench

Prepared 2026-09-11 at the owner's request to end the token-heavy multi-agent workflow. This is a preservation/publication handover, not sprint acceptance or a release. Use one Pro AI by default; do not restart role chains or commission another broad audit merely to reconstruct context. The owner wants remaining product outcomes identified, leaving implementation choices to the successor.

Read this file first, then [remaining outcomes](REMAINING.md). Consult [publication and provenance](PUBLICATION.md) only for source/history questions. Read deeper files selectively, not all logs, snapshots or chat history.

## Original objective

Build PbiBench (also called PBI Workflow Pro), a focused Power BI model-and-report engineering workbench on open-source Tabular Editor 2: model navigation, DAX, automation, project/Git workflow, report lineage/themes, exploration and optimization. Preserve the TE2 editing engine and upstream compatibility. This is an evolving foundation, not a completed full app.

## What exists and why

- Inherited TE2 WinForms host, model/property editing, TOMWrapper, undo, serialization, C# scripting and BPA remain the foundation.
- v0.1 added neutral Core contracts, project discovery/context and Git parsing primitives, formatter capability policy, smoke checks and CI. Discovery primitives still have known gaps before Project UI use.
- v0.2 added semantic Quick Open (Ctrl+P), semantic dependency navigation, and a DAX Workbench with an AMO query adapter.
- Subsequent stabilization and S001 added local UTF-8 DAX documents, selection execution, bounded transient history, dedicated query-session lifecycle and cleanup, safe diagnostics, retained-result budgets, shared explicit session formatter consent, CSV export failure handling, regression harnesses and a reproducible verification script.
- Rework corrected floating-point precision loss on net48 and delayed formatter responses modifying replacement documents; expression/model/property context guards were strengthened too.

The workbench currently has one result set, explicit local saves, transient history and a plain-text query editor. Defaults are 5,000 rows, requested 120-second timeout, estimated 32 MiB retained results, 256 columns and 1 MiB retained string/binary cells. These are application retention limits, not server-work or total-process memory guarantees. Timeout is best effort in-process, not guaranteed hard termination.

## Exact stopping point

S001 A/B/C and correction batch 01 are implemented. Independent correction QA passed. BUG-001 and BUG-002 are QA_VERIFIED, awaiting a focused lead decision. The last recorded lead decision remains REWORK_REQUIRED; it predates the correction QA. No sprint has been accepted. Do not reimplement the two fixes merely because the older decision says rework.

[Latest correction QA](../../projectmanagement/handoffs/S001-QA-REWORK-01.md) is the best detailed entry point. [Developer handoff](../../projectmanagement/handoffs/S001-DEVELOPER.md) contains implementation and exact commands. [Lead decision](../../projectmanagement/reviews/S001-DECISION.md) records the reasoning and acceptance limits. Earlier README/STATUS paragraphs are historical where they conflict with this dated takeover or latest QA.

## Evidence, without overstating it

Historical 2026-09-08 evidence: fresh Release host rebuild and actual-host smoke PASS; corrected T14/T15 plus affected tests PASS; extended QA includes 8,260 numeric round-trip cases and document/expression-context transitions. Prior Core net9 diagnostic PASS, but canonical net10 was BLOCKED_ENV with installed SDK 9.0.101. Legacy TE2 tests were blocked by references; selected TOM tests were 8 PASS / 3 endpoint-dependent failures classified BLOCKED_ENV. Native UI evidence is limited to opening a synthetic BIM, Ctrl+P and navigating to Margin.

No live Power BI Desktop/XMLA/authentication certification, full native keyboard/document/DPI journey, clean-checkout remote CI success or release acceptance exists. OBS-001 (intermittent second-save IOException) is unresolved, low severity and not reproduced by later QA.

This handover did not rebuild or rerun product tests. Its source hash check found 48/51 exact matches against the correction-QA manifest; the three differences are BACKLOG, REGISTERS and STATUS updates after QA. Product/test source in that manifest matched. See [source check](evidence/source-check.json). Publication may normalize Git line endings; raw historical SHA256 values identify the original local bytes.

## Essential boundaries

Neutral logic belongs in PbiBench.Core; TOM/AMO/WinForms adapters in TabularEditor/PbiBench. TE2 owns semantics/undo/serialization. Necessary upstream edits are recorded in docs/pbibench/UPSTREAM_TE2_SYNC.md. Formatter consent is explicit, session-only, deny-by-default with admin disable; no model telemetry. Trusted arbitrary C# is not sandboxed. Disk/Loaded/Live/Git state must not be treated as interchangeable. Never persist credentials, business rows or private DAX in management evidence.

Fabric administration, agent orchestration, deep DAX plans/timings and final report rendering remain external product/tool responsibilities. Detailed architecture exists in projectmanagement/ARCHITECTURE.md; read only the relevant area when working on it.

## Suggested first prompt

> Take over this repository from handover/2026-09-11/README.md and REMAINING.md. Work as a single Pro AI unless I request delegation. Preserve existing work. Start from the published takeover branch, assess the pending correction acceptance and prioritize remaining product outcomes. Use existing evidence instead of repeating the entire audit; verify only what your changes or unresolved risks require. Keep progress notes compact. Do not claim the app complete or live-tested without the outstanding acceptance evidence. Decide how to implement the remaining work yourself.
