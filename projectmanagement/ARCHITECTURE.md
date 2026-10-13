# Product vision and technical direction

Decision date: 2026-09-08. Owner: technical lead.

## What success looks like

A model engineer can open a Power BI model/project, understand objects and their
dependencies, write and validate DAX, inspect bounded query results, automate
reviewable changes, reconcile project files, and understand report usage without
losing trust in the original TE2 editing engine. The value is a connected
engineering workflow with understandable changes and recoverability.

Retain the recognizable TE2 WinForms host. The seven areas below are capabilities,
not a requirement to build seven tabs or a new shell immediately. Improve the
existing dialogs and seams before committing to a workspace redesign.

| Area | Why it matters | Inherited/existing assets | Target capability |
|---|---|---|---|
| MODEL | Find and understand the semantic model before changing it | TOMWrapper, tree/property editing, dependencies, undo, Quick Open, Semantic View | Reliable navigation, object/dependency context, later relationship and report usage views |
| DAX | Shorten the authoring/query feedback loop | TE2 expression editor/lexer; new plain-text query workbench and local documents | Shared language services, completion/signatures/diagnostics, dependable execution, multiple results, recoverable documents |
| AUTOMATE | Make repeated model changes reproducible | TE2 C# compiler/editor, scripts, custom actions, batching | Script workspace; restricted recipe preview/diff/apply; separately trusted arbitrary C# |
| PROJECT | Prevent disagreement between disk, loaded model, live model, and Git | TE2 PBIP/TMDL load/save; neutral discovery/context helpers | Visible state identities, external-change detection, reviewed synchronization, recovery, context export |
| REPORT | See what model changes affect in report source | Folder discovery only in this branch | PBIR indexing/lineage first; version-gated reviewed report/theme edits later |
| EXPLORE | Understand data while engineering the model | Existing live connection and query primitives | Bounded previews, typed filtering, then profiling/coverage and Pivot Lab |
| OPTIMIZE | Base improvements on evidence | Existing BPA engine, rules/fixes/severity | Curated rules, assertions and storage/usage evidence with tested applicability |

## Target ownership and dependency flow

```text
TE2 WinForms shell + PbiBench views
               |
      PbiBench application/host adapters (net48)
        |           |                |
        |           |                +--> file/Git/remote formatter adapters
        |           +--> dedicated AMO query session
        +--> TE2/TOMWrapper editing, undo, serialization, lexer, BPA, C#
               |
      PbiBench.Core (netstandard2.0)
      contracts, bounded algorithms, neutral snapshots, policies,
      document rules, reviewed change-plan descriptions
```

Dependencies point from the host toward Core; Core cannot import the host.
The diagram shows ownership, not that TOMWrapper must depend on Core. Core remains
free of WinForms/TOM/AMO/HTTP/authentication/AI-provider dependencies. Small explicit
local document I/O already exists in Core (`DaxDocument`); preserve compatibility
and isolate I/O behind testable boundaries as complexity grows. Do not call Core
entirely pure while it performs filesystem operations.

Create further PbiBench libraries only when a cohesive subsystem needs separate
dependencies or reuse. Do not create an empty project per feature. Any future
report sidecar is an explicit architecture decision with a versioned protocol,
failure/restart behavior, and a clear deployment benefit.

## Approved decisions

**ADR-001 — Reuse TE2 as the semantic owner.** TOMWrapper owns object semantics,
undo/redo, dependency changes and serialization. New UI/adapters use existing
services; they do not maintain a second mutable semantic model. Snapshot models
are read-only evidence and carry source identity. This keeps behavior consistent
and upstream fixes feasible.

**ADR-002 — Keep the integration additive.** Use `TabularEditor/PbiBench/**`,
neutral Core, and `Directory.Build.targets`. Small changes to upstream-owned
request boundaries are allowed when necessary to enforce a cross-cutting policy;
record file/reason/conflict risk. Do not let a preference for zero upstream edits
leave an actual privacy boundary unenforced.

**ADR-003 — One execution owner per run.** The query adapter owns a dedicated
session, cancellation, reader, and cleanup. The UI never uses the editing
connection for queries or cancellation. A token signals intent, not proof that a
blocking provider call stopped. Do not free the admission slot and allow repeated
queries while abandoned work still consumes sessions. No thread aborts or
unbounded background tasks. Core retains transport-neutral results and reasons.

**ADR-004 — Bounds describe what they actually bound.** Row count alone is not a
memory limit or a server-work limit. Add retained-result budgets, column/cell
limits and clear truncation reasons. Provider buffering and one oversized value
can precede application checks; report that limitation. Exact result-memory or
hard wall-clock isolation needs a separate design if the in-process provider
cannot guarantee it.

**ADR-005 — One formatter policy boundary.** In this PbiBench build, repository
formatter entry points must use a shared deny-by-default gate before any request,
including redirect discovery. Administrative disable wins. DAX consent is
separate from telemetry; the existing `CollectTelemetry=true` setting is not new
PbiBench consent. S001 uses explicit process-session consent and omits model
telemetry entirely. Restart revokes consent; durable consent/telemetry UI can be
planned later. Script/CLI calls never create hidden consent dialogs. Reuse
existing single/batch and UDF wrappers where valid. This boundary governs the
repository's formatter service, not arbitrary network calls in trusted C#.

**ADR-006 — Consistent language ownership.** TE2 already owns highlighting/token
and dependency facilities. Before porting the older language service, map one
owner for each of tokenization, parsing, metadata, completion, signatures,
diagnostics and navigation. Share behavior between query and expression editing
through adapters; do not replace the working expression editor speculatively.
Local diagnostics are advisory and must not be presented as engine certification.

**ADR-007 — Separate recipe safety from C# trust.** A restricted, allowlisted
recipe can propose exact metadata deltas against a snapshot, validate a
fingerprint, and apply through TE2 undo. Arbitrary C# runs with the user's process
permissions and can have external effects; a model snapshot is not a sandbox or
complete rollback. Present these lanes separately and test the boundary.

**ADR-008 — Explicit project state and reviewed writes.** Disk, Loaded, Live,
Git, and Baseline have separate identities; do not silently equate them. New
bulk/project/report writes require a proposal, review, stale check, apply and
undo or recovery. A single-file temp replacement is not a multi-file transaction.
Project discovery must handle multiple PBIPs without silently choosing the wrong
one, use bounded traversal, and preserve filesystem roots. Unknown report schemas
stay read-only. A rename cannot be called safe when lineage is unresolved.

**ADR-009 — Recovery and privacy are deliberate.** Memory history is transient.
The current explicit document saves remain user-driven; future autosave/recovery
requires disclosed locations, retention and deletion policy. Project-context JSON
never includes credentials, connection strings or business rows. Diagnostics use
safe categories/allowlisted fields; truncating raw exception text is not redaction.

**ADR-010 — Tests have distinct authorities.** Core checks prove neutral logic;
host checks prove the real compiled WinForms integration; a controlled transport
can prove race handling but cannot certify AMO/Desktop/XMLA behavior. Live checks
must identify endpoint class/version and source revision. The lead decides
acceptance after independent QA and code-logic review.

## Delivery sequence and boundaries

1. **S001 reliability:** query lifecycle/bounds/diagnostics, formatter gate, export
   failure handling, reproducible checks, and acceptance of pending stabilization.
2. **DAX authoring foundation:** consistent language-service ownership, model
   completion/signatures/navigation and a tested valid/invalid DAX corpus.
3. **DAX workspace and automation:** multiple results/documents/recovery as
   separately bounded increments; script documents and trusted execution UX;
   then restricted recipes with stale checks and reviewed apply.
4. **Project engineering:** discovery repair and read-only project/Git state,
   then conflict-aware synchronization and recovery. Early read-only project work
   may move ahead if it unblocks test fixtures; no automatic scope expansion.
5. **Report engineering:** PBIR schema-aware read-only index/lineage, then reviewed
   edit/theme plans with restore and Desktop reopen acceptance.
6. **Explore/Optimize:** bounded previews reuse the accepted execution service;
   profile/Pivot/storage evidence and rule packs follow measured needs.

Only S001 is currently authorized in detail. Future sprint numbers and bundles
are decided at review; version labels from older docs are product groupings, not
hard sprint deadlines. Backlog dependencies, risks and user feedback govern order.

Keep Fabric administration in Fabric Toolbox, MCP/agent orchestration outside the
core product, deep timings/plans in DAX Studio, final rendering in Desktop, and
visual theme simulation in Theme Forge. No debugger/NuGet/project-system rewrite,
full Git client, WPF shell import, unrelated package upgrade or proprietary TE3
implementation copying is included.

## Stable-product gate

### Lead review refinements — S001, 2026-09-08

**ADR-011 — Numeric fidelity is explicit.** Shared grid/CSV representation must
preserve finite Single/Double values on the actual net48 runtime. Invariant culture
alone does not ensure precision; use explicit G9/G17 respectively. Keep retained
values typed. Display rounding, if introduced later, must not change raw export.
Document special numeric values/zero policy; CSV is not a binary serialization.

**ADR-012 — Async editing context has identity and revision.** Every successful
document replacement advances a logical generation even when text is identical.
Formatting captures generation plus content and consent revisions. Failed/cancelled
replacement preserves context. Save/Save As persist the same editing buffer;
successful formatting afterward makes that buffer dirty again. Expression
formatting similarly identifies model, object and DAX property, including
away/back transitions. A reused object reference or matching text/path alone is
not sufficient context evidence. No generic workspace framework is required.

**ADR-003 clarification:** in-process dedicated sessions with best-effort timeouts
are the approved initial architecture. Retain admission through cleanup; do not
claim hard wall-clock termination. Live/auth support remains gated separately.
Worker-process isolation needs a demonstrated recovery requirement or an explicit
hard-deadline requirement, not merely a blocked test environment.

**ADR-005 clarification:** consent governs dispatch admission; already admitted
or in-flight requests may have sent data when consent is revoked. Discard stale
UI responses and deny new admissions. Do not hold a UI-shared lock through blocking
network I/O to attempt an impossible retroactive transmission guarantee.

### Release evidence

Before a v1.0 claim, verify actual PBIP/TMDL open/edit/undo/save/reopen, Desktop
query/error/cancel/timeout/session preservation, supported XMLA authentication,
Git external-change/recovery workflows, report/theme apply/restore/Desktop reopen,
and specialist-tool handoff. Include representative large models, keyboard and
real DPI interaction, privacy checks and clean-checkout CI. Each unsupported or
unavailable capability remains explicitly limited in product claims.
