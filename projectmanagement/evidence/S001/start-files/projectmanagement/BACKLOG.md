# Feature and defect backlog

Owner: technical lead for scope/order; light tester for factual status/evidence.
Updated: 2026-09-08. Product areas are defined in ARCHITECTURE.md.

Priority: P0 blocks dependable/current promised behavior; P1 next product value;
P2 later value; P3 exploratory. Priority is delivery order, not a security rating.
Feature state uses WORKFLOW.md. Dependencies mean accepted foundations unless the
lead explicitly permits parallel independent work.

## Active sprint and known baseline

| ID | Priority / state | Outcome and reason | Depends on / owner | Acceptance evidence |
|---|---|---|---|---|
| FND-001 | P0 READY, S001-A | Reproducible host/Core/CI verification; avoid stale-binary and SDK false positives | Existing build; Dev then QA | T01–T03; clean-checkout CI or explicit pending gate |
| FND-002 | P0 IMPLEMENTED, S001-A/C acceptance | Preserve pending build/layout fixes, query selection/history, local documents | Pre-existing dirty changes; QA | Fresh host smoke passed at baseline; T04/T09/T10 plus native interaction still required |
| FND-003 | P1 READY, S001-C | Align published feature status and upstream ledger with evidence | All S001 passes; Dev, QA maintains | No "no adapter yet"/untested readiness contradiction |
| DAX-001 | P0 READY, S001-B | Dedicated session lifecycle; responsive cancel; truthful timeout and cleanup ownership | FND-001; Dev | T05/T11/T12; lead lifetime review |
| DAX-002 | P0 READY, S001-B | Safe diagnostic categories without raw provider secrets | DAX-001 seam; Dev | T06 sentinel tests and UI/history/log inspection |
| DAX-003 | P1 READY, S001-B | Bound retained data beyond rows; disclose result limits | DAX-001; Dev | T07 boundary tests; no false total-memory/server-work claims |
| DAX-004 | P0 READY, S001-C | Actual formatter consent boundary, no model telemetry, existing path coverage | FND-001 and ADR-005; Dev | T08 single/batch/script/expression/Workbench transport evidence |
| DAX-005 | P1 READY, S001-C | CSV failures preserve app state and destination; stable data representation | Existing grid/export; Dev | T09 culture, quoting, failure-injection and UI cases |
| DAX-006 | P0 PLANNED, S001 gate | Live Desktop/XMLA acceptance including editor-session survival | DAX-001–005; QA | T11/T12; currently NOT_RUN, environment availability to establish |

## Ordered feature roadmap

| ID | Priority / state | Outcome and why | Dependencies / likely grouping | Acceptance gate |
|---|---|---|---|---|
| DAX-010 | P1 PLANNED | Language-service ownership map and corpus; avoid two disagreeing parsers | S001 review; next sprint candidate | Design map, licensing/provenance and corpus test matrix before selective port |
| DAX-011 | P1 PLANNED | Model/function completion, signatures, definition navigation and diagnostics | DAX-010 | Quoted names, variables, comments, UDFs, valid/invalid corpus; stale model invalidation; undo/keyboard checks |
| DAX-012 | P1 PLANNED | Multiple result sets and per-set selection/export; avoid silently ignoring results | DAX-001/003 | Zero/one/many sets, duplicate column names, limits across sets, cancel between sets |
| DAX-013 | P1 PLANNED | Multi-document DAX workspace and explicit recovery; preserve longer work | Accepted existing document rules | Dirty/close-all/save failure, recovery retention/opt-in and stale-file matrix |
| DAX-014 | P2 PLANNED | DAX Studio handoff for deep analysis | Accepted connection context | Supported launch/context mapping; no credential logging; real handoff |
| MODEL-001 | P1 IMPLEMENTED, partial verification | Quick Open and semantic dependency navigation reduce discovery cost | Existing snapshots/TE2 Goto | T04/T10; actual model navigation, hidden objects, rename/delete freshness |
| MODEL-002 | P2 PLANNED | Relationship/usage visualization that explains evidence | MODEL-001; REPORT-001 for report usage | Large model navigation, unresolved references displayed, no model mutation |
| AUTO-001 | P1 PLANNED | Multi-tab .cs/.csx, diagnostics and trusted execution UX | Accepted document architecture; TE2 compiler | Save/recovery/conflict corpus; Problems navigation; explicit trust; batching/undo regression |
| AUTO-002 | P1 PLANNED | Restricted Safe Recipe preview/diff/apply | ADR-007; model identity and undo adapters | Allowlist rejection, exact diff, stale fingerprint rejection, atomic intent and undo/failure behavior |
| AUTO-003 | P2 PLANNED | Recorder/macros/curated gallery reduce repetitive work | AUTO-001/002 | Repeatable recipe output, compatibility metadata, preview fidelity; unsafe recipes labeled trusted |
| PROJ-001 | P1 PLANNED | Repair bounded discovery, root handling and multiple-PBIP selection | Existing Core helpers; before visible Project UI | Root/UNC, junctions, depth/width/access denied, explicit PBIP links/ambiguity fixtures |
| PROJ-002 | P1 PLANNED | Read-only Project panel, actual Git state and neutral context export | PROJ-001 | Missing Git, dirty/untracked/renamed files, worktree .git file, argument safety, credential-free JSON |
| PROJ-003 | P1 PLANNED | Disk/Loaded/Live/Git/Baseline comparisons and external-change detection | PROJ-002; ADR-008 | No silent overwrite; distinct identities; modify/delete/rename/reload conflict matrix |
| PROJ-004 | P1 PLANNED | Reviewed synchronization with recovery and VS Code handoff | PROJ-003 | Exact plan/stale rejection/partial failure/restore/save-reopen integration |
| REPORT-001 | P1 PLANNED | Read-only PBIR index, schema status and semantic lineage | PROJ-001/002; model snapshots | Real versioned fixtures; unresolved refs explicit; unknown schema read-only |
| REPORT-002 | P2 PLANNED | Reviewed PBIR changes with backup/atomic writes/restore | REPORT-001; PROJ-004 principles | Stale plan, interrupted multi-file apply, restore, Desktop reopen |
| REPORT-003 | P2 PLANNED | Theme validation/apply and Theme Forge exchange | REPORT-001/002 | Unknown properties/version behavior; theme apply/restore/reopen; versioned exchange |
| EXP-001 | P1 PLANNED | Bounded table preview and typed filters/sorts | Accepted DAX lifecycle/budgets | Identifier escaping, typed query generation, deterministic limits, live test |
| EXP-002 | P2 PLANNED | Profiling, distributions, relationship coverage and Pivot Lab | EXP-001 | Null/blank semantics, cardinality/skew fixtures, query cost limits, stale context |
| OPT-001 | P2 PLANNED | Curated BPA packs/preferences and semantic assertions | Existing BPA; metadata versioning | Rule applicability/false positives, severity/suppression, reviewed fixes/undo |
| OPT-002 | P2 PLANNED | VPAX/DMV/storage evidence and usage-aware recommendations | DAX lifecycle; REPORT lineage where used | Provenance/time/source context, unsupported endpoints, bounded queries; no universal rewrite claims |
| REL-001 | P0 PLANNED, v1.0 gate | Real integration matrix and honest supported-feature claims | Delivered roadmap scope | Architecture's stable-product gate with no hidden unresolved failures |

## Deferred decisions and known risks

| ID | State / owner | Decision or risk | When to revisit |
|---|---|---|---|
| R-001 | OPEN, Lead | AMO connect/command cancellation may lack a strict in-process deadline | S001-B evidence; escalate before choosing another transport/process |
| R-002 | OPEN, QA | Live Desktop/XMLA/auth/DPI environments not yet established | QA preparation; batch manual requirements for user |
| R-003 | OPEN, Dev/QA | net10 harness vs local SDK9; upstream legacy test references; build warnings | S001-A; do not conflate these issues or retarget casually |
| R-004 | OPEN, Lead | Single-file hash-check then replacement has a concurrent-writer race; not crash recovery | Before recovery/synchronization claims; keep current limitation documented |
| R-005 | OPEN, Lead | Memory budget does not bound provider buffering/one huge cell or server work | S001-B tests and product claims; isolation only if justified |
| R-006 | DEFERRED, Lead | Old `D:\PROJ\powerbi_enhanced_dev` components are port candidates, not accepted features | Each selective port; inspect dependencies/tests/license before importing |
| R-007 | OPEN, QA | Documentation overstates discovery boundedness and has stale v0.2 inventory | S001-C docs; PROJ-001 implementation before host wiring |
| R-008 | DEFERRED, Lead | Offline formatter, persistent consent, optional telemetry settings | Only after a tested compatible design and clear user value |

Tester: add new defects as `BUG-001`, `BUG-002`, etc., with severity, reproduction,
affected source identity, owner and regression test ID. Do not renumber existing
IDs or move deferred work into the active sprint without a lead decision.

## Acceptance history

No sprint has been accepted under this process yet. The baseline review validates
specific checks, not the whole product. Append sprint decision links here at close.
