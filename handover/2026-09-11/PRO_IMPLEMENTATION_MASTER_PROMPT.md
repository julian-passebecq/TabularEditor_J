# TE2 Enhanced - Pro implementation master prompt

You are the primary implementation model for the current TE2 Enhanced branch. Your job is to advance the product substantially before handing it to a separate Light-model QA conversation.

## 1. Product and branch

Repository: `julian-passebecq/TabularEditor_J`

Working product: **TE2 Enhanced** - a focused, lighter Power BI engineering extension built directly on the open-source Tabular Editor 2 fork.

Working branch: `codex/pro-ai-handover-2026-09-11`

Do not work from `master` and do not move this effort into `powerbi_enhanced_dev`. The older/full PbiBench application is a different project. Some legacy names inside this repository still say PbiBench; preserve compatibility where useful, but keep this product a focused TE2-based enhancement.

Before editing, inspect and preserve local state. If the local checkout contains work newer than the branch, reconcile it carefully rather than resetting or discarding it.

## 2. Read first

Read:

1. `AGENTS.md`
2. `handover/2026-09-11/README.md`
3. this file
4. `handover/2026-09-11/REMAINING.md`
5. `projectmanagement/STATUS.md`
6. `projectmanagement/ARCHITECTURE.md`
7. `projectmanagement/BACKLOG.md`
8. `projectmanagement/handoffs/S001-QA-REWORK-01.md`
9. `projectmanagement/handoffs/S001-DEVELOPER.md`
10. `projectmanagement/reviews/S001-DECISION.md`

Read deeper historical evidence only when it is necessary to understand an implementation or defect. Do not spend the whole run reconstructing old management history.

## 3. Division of labor

### Pro owns

- architecture/code logic review;
- implementation and refactoring;
- finishing coherent product capabilities;
- adding focused regression tests for new logic;
- keeping builds/smokes green enough to hand off responsibly;
- updating compact implementation/handoff documentation.

### Light owns after Pro

A separate Light-model conversation will perform the exhaustive independent Windows QA campaign: clean rebuild, full automated matrix, real application UI journey, keyboard/focus/DPI, regression testing, screenshots/logs, live endpoint testing when approved, and final GREEN/YELLOW/RED release recommendation.

Therefore do not consume most of the Pro run on repetitive manual testing. Run the tests required to develop safely, fix your own regressions, and leave a precise risk-based QA handoff.

## 4. First action: close the stale S001 decision correctly

S001 A/B/C and correction batch 01 are already implemented. BUG-001 (Single/Double CSV fidelity) and BUG-002 (stale formatter response modifying a replacement document) are QA-verified.

Do not reimplement them because the older `S001-DECISION.md` still says `REWORK_REQUIRED`.

Perform a focused logic/code review of the corrected source and QA evidence. If the fixes are sound, update project status/decision records so that:

- corrected implementation is accepted for the scope actually evidenced;
- live Desktop/XMLA/authentication, canonical clean-environment, full native UI and other unexecuted gates remain explicitly OPEN/BLOCKED rather than being falsely accepted;
- no claim is made that the whole final product or v1.0 is released.

If you discover a real remaining code defect in S001, fix it and add focused coverage before continuing.

## 5. Work continuously through the roadmap

Do not stop after a plan, one review, or one minor fix. Continue through successive coherent workstreams as far as the repository, environment and context permit. Commit coherent checkpoints. Prefer useful vertical slices to broad empty scaffolding.

Use the dependency order below unless code inspection reveals a stronger reason to adjust it.

---

# WORKSTREAM A - Build/release engineering foundation (REL-001 / FND)

Make the development/release lane reproducible enough that later capabilities are not built on stale binaries or accidental local state.

Goals:

- make canonical supported Core checks build with the intended SDK/toolchain;
- preserve actual net48 Tabular Editor host build and smoke coverage;
- repair or clearly isolate the legacy TE2 regression lane rather than casually retargeting it;
- make `Scripts/Verify-PbiBench.ps1` truthful, deterministic and useful on a supported development machine;
- ensure CI definitions cover the actual enhanced host/Core integration without claiming live endpoint coverage;
- remove stale source-inventory/document contradictions that can misroute future agents;
- keep upstream TE2 changes explicitly recorded in `docs/pbibench/UPSTREAM_TE2_SYNC.md`;
- do not perform unrelated package/framework upgrades.

This is infrastructure work in service of implementation, not an excuse to spend the whole pass on CI if useful product work can proceed.

---

# WORKSTREAM B - DAX authoring foundation (DAX-010 / DAX-011)

Build a coherent language-service layer without replacing working TE2 editing behavior speculatively.

First establish ownership/provenance for:

- tokenization/highlighting;
- parsing or syntax structure;
- model metadata lookup;
- completion;
- signatures;
- definition navigation;
- diagnostics.

Then implement practical authoring improvements shared where sensible between the TE2 expression editor and DAX query workspace:

- model table/column/measure completion;
- DAX function completion;
- function signatures/parameter help;
- definition/go-to navigation;
- advisory local diagnostics;
- stale-model invalidation after rename/delete/change;
- correct handling of quoted table/object names, comments, variables and supported UDF/function constructs.

Create a valid/invalid DAX corpus and focused tests. Do not label local diagnostics as engine certification.

Preserve privacy: authoring assistance must not silently send model/DAX content remotely.

---

# WORKSTREAM C - DAX workspace depth (DAX-012 / DAX-013 / DAX-014)

Extend the existing Workbench instead of replacing it with a new shell.

### Multiple result sets

Support zero/one/many result sets with:

- independent grids/tabs or an equally clear selector;
- per-result selection/copy/export;
- duplicate column names handled safely;
- clear truncation/reason state per set;
- retained-result budgets applied honestly across result sets;
- cancellation/cleanup between sets;
- no silent loss of secondary result sets.

### Multiple DAX documents

Add a dependable multi-document workflow:

- New/Open/Save/Save As/Close/Close All;
- dirty indicators;
- conflict/external-change detection;
- failed-save behavior that preserves edits;
- explicit recovery if implemented, with disclosed local storage and retention/deletion rules;
- logical document generation/revision semantics consistent with the fixed formatter stale-response guard;
- no silent overwrite.

### DAX Studio handoff

Implement a real specialist-tool handoff rather than embedding DAX Studio:

- configurable/discoverable executable;
- safe argument construction;
- connection/server/database mapping where supported;
- selected/current query handoff;
- no credentials or secrets in logs/arguments beyond what the supported DAX Studio interface necessarily requires;
- useful unavailable/not-installed state.

Deep timings/query plans remain DAX Studio's responsibility.

---

# WORKSTREAM D - Model navigation and understanding (MODEL-001 / MODEL-002)

Harden existing Quick Open/dependency navigation first:

- hidden-object behavior;
- rename/delete freshness;
- stale snapshot handling;
- large-model responsiveness;
- keyboard navigation and focus behavior where implementation changes touch it.

Then add a useful read-only relationship/usage view that helps answer:

- how tables are related;
- cardinality/filter direction/state;
- what objects depend on a selected object;
- unresolved references rather than hiding them;
- later report usage when REPORT-001 evidence exists.

Do not create a second mutable semantic model. Use TE2/TOMWrapper as semantic authority and read-only snapshots for visualization/evidence.

---

# WORKSTREAM E - Automation (AUTO-001 / AUTO-002 / AUTO-003)

Keep arbitrary C# and restricted recipes as explicitly different trust lanes.

### Script workspace

Build a practical multi-document `.cs` / `.csx` workspace around the existing trusted TE2 compiler/execution model:

- tabs/documents;
- open/save/save-as/dirty/conflict handling;
- diagnostics/problems navigation;
- explicit trusted-execution UX;
- batching/undo integration where the existing TE2 semantics support it;
- recovery rules consistent with DAX documents where appropriate.

Do not pretend arbitrary C# is sandboxed.

### Safe recipes

Implement a restricted metadata recipe path with:

- allowlisted operations only;
- read-only source snapshot and fingerprint;
- exact proposed diff/change plan;
- explicit review before apply;
- stale fingerprint rejection;
- apply through TE2 semantic/undo ownership;
- clear partial-failure/atomicity semantics;
- regression coverage for rejected operations and stale plans.

### Macro/gallery layer

Only after the above is sound, add curated/repeatable actions that reuse the trusted C# or safe-recipe lanes with explicit metadata about trust, compatibility and preview behavior.

---

# WORKSTREAM F - Project discovery and engineering (PROJ-001..004)

Repair discovery before presenting it as dependable UI.

### Discovery

Handle:

- filesystem roots correctly;
- UNC/network path forms where feasible;
- bounded depth/width traversal;
- junction/reparse loops;
- access denied/unreadable directories;
- multiple PBIP projects with explicit user selection;
- no silently chosen project.

### Read-only Project/Git panel

Expose useful current context without turning the app into a full Git client:

- selected PBIP/project root;
- loaded model identity;
- Disk vs Loaded vs Live vs Git vs Baseline identities;
- actual Git branch/status/dirty/untracked/renamed state;
- worktree `.git` file handling;
- safe VS Code/open-folder handoff;
- credential-free bounded context export.

### External-change/conflict handling

Detect file modify/delete/rename/reload cases. Never silently overwrite a changed external source.

### Reviewed synchronization

When writing/synchronizing:

- create an explicit plan;
- fingerprint/stale-check sources;
- preview the changes;
- apply with understood per-file semantics;
- retain recovery/restore information;
- handle partial failure explicitly;
- validate save/reopen where implementation can do so offline.

A temp-file replacement is not automatically a multi-file transaction.

---

# WORKSTREAM G - Report engineering (REPORT-001..003)

Start read-only and version-aware.

### PBIR index / lineage

Implement:

- bounded PBIR discovery/indexing from the selected project;
- schema/version recognition;
- page/visual/semantic binding inventory where supported;
- lineage from report references to semantic objects;
- explicit unresolved references;
- unknown/new schemas read-only with diagnostics rather than guessed mutation.

### Reviewed report changes

Only for explicitly supported schema/contracts:

- typed proposed changes;
- preview/diff;
- stale check;
- backup/recovery;
- atomic file replacement where achievable;
- clear multi-file partial-failure semantics;
- restore path;
- leave Desktop final rendering/reopen validation for Light/manual QA when the environment is available.

### Theme

Add schema/version-aware theme validation and reviewed application/exchange. Preserve unknown properties where the supported contract permits; do not invent pixel-perfect rendering inside this app. Theme Forge remains the specialist visual-design tool where appropriate.

---

# WORKSTREAM H - Explore (EXP-001 / EXP-002)

Reuse the accepted DAX execution/session/budget architecture.

### Table preview

Add bounded data exploration with:

- safe identifier quoting/escaping;
- typed filter construction;
- deterministic row/result limits;
- sorting with explicit semantics;
- truthful query cost/limit messaging;
- stale context protection.

### Profiling / distributions / Pivot Lab

If time permits after the bounded preview is solid, add:

- null/blank counts;
- cardinality/distribution summaries;
- relationship coverage indicators;
- bounded pivot/exploration flows;
- explicit source/time/provenance and query limits.

Do not imply that exploratory queries are free or that application result limits bound server work.

---

# WORKSTREAM I - Optimize (OPT-001 / OPT-002)

Build on the existing BPA instead of replacing it.

### BPA packs and assertions

Add curated rules/preferences/semantic assertions with:

- applicability metadata;
- severity;
- suppression/preferences;
- low false-positive emphasis;
- reviewed fix previews;
- TE2 undo integration;
- version/provenance information.

### Storage/usage evidence

Where supported, add bounded VPAX/DMV/storage/usage evidence with:

- endpoint/source/time provenance;
- bounded queries;
- explicit unsupported endpoint behavior;
- recommendations tied to evidence, not universal rewrite claims;
- report usage only when lineage is trustworthy.

Deep performance traces/plans remain external specialist-tool work.

---

# WORKSTREAM J - Integration and release-candidate preparation

As the implementation matures, make the features feel like one focused enhancement of TE2 rather than disconnected experiments.

Required integration principles:

- preserve standard TE2 model editing, property grids, undo, serialization, scripting and BPA behavior;
- keep navigation discoverable without adding a bloated full-workbench shell;
- reuse document/conflict/change-plan conventions across DAX, scripts, projects and reports;
- centralize shared status/identity models where they are truly common;
- remove duplicate/stale prototype routes rather than leaving two competing ways to do the same task;
- keep feature availability truthful when an endpoint/tool/schema is unsupported.

Update documentation/catalogs to distinguish:

- implemented and ready for Light QA;
- implemented but environment-gated;
- explicitly deferred;
- external specialist responsibility.

Do not claim v1.0 or merge to `master` yourself.

## 6. Coding quality expectations

- Prefer small cohesive services and adapters over UI code containing all logic.
- Add CancellationToken to new meaningful async I/O boundaries.
- Keep public error messages useful without leaking secrets/provider internals.
- Bound file sizes, collection sizes and traversal where untrusted/large local project input is involved.
- Validate paths and external-process arguments.
- Preserve exact user content where fidelity matters; avoid destructive normalization.
- Use atomic/replacement patterns where justified and document the crash/concurrency limits honestly.
- Preserve upstream licenses and third-party provenance.
- Record intentional upstream TE2 source edits.
- Avoid speculative abstractions/frameworks that add more code than the feature requires.

## 7. Test expectations during Pro development

For every workstream changed:

- add focused tests for neutral algorithms/contracts;
- add host/integration smoke coverage when host wiring changes;
- build the actual changed projects/host;
- run the smallest relevant regression set plus any test you add;
- fix regressions caused by your work before moving on.

Periodically run the repository verification script or the broad automated subset when enough work has accumulated. Do not spend large amounts of time repeating manual DPI/keyboard/live-environment matrices; Light is responsible for independent final QA.

If live Desktop/XMLA/auth fixtures are unavailable, leave those tests as explicit Light/user-environment gates. Do not use unknown local server processes or credentials without approval.

## 8. Git/commit behavior

Stay on the takeover branch unless the local workspace proves a newer continuation branch exists.

Create coherent commits after meaningful vertical slices. Use descriptive messages. Do not squash away useful history, merge to `master`, force-push, reset or clean away preserved work.

Before each large workstream, check the tree for unrelated local changes and avoid accidentally committing them.

## 9. What not to build in this pass

Unless a directly required dependency forces a small bridge, do not spend time on:

- full Fabric tenant administration;
- MCP/agent orchestration;
- embedded DAX Studio source/UI;
- proprietary TE3 feature/code copying;
- a WPF replacement shell;
- a full Git client;
- binary PBIX reverse engineering;
- a custom final Power BI renderer;
- unrelated NuGet/framework modernization;
- a broad migration from WinForms just for aesthetics.

## 10. Required final handoff to Light

Before stopping, create/update:

### `handover/2026-09-11/PRO_IMPLEMENTATION_STATUS.md`

Include:

- starting and ending branch/HEAD;
- commits created;
- completed features by backlog ID;
- architecture decisions/change notes;
- files/subsystems with highest regression risk;
- targeted tests/builds Pro ran and their results;
- known defects;
- environment-gated items;
- remaining roadmap in priority order.

### `handover/2026-09-11/LIGHT_QA_HANDOFF.md`

Give Light an executable QA plan:

- exact branch/HEAD to test;
- prerequisite SDK/tool versions;
- clean restore/build commands;
- verification script commands;
- actual enhanced Tabular Editor executable path;
- representative local/synthetic fixtures;
- feature-by-feature manual UI journeys for everything Pro changed;
- regression checks for ordinary TE2 editing;
- keyboard/focus/DPI/resize checks;
- external-change/conflict/recovery scenarios;
- DAX result/query/formatter/privacy edge cases;
- DAX Studio/VS Code/Desktop handoffs where available;
- live Desktop/XMLA/auth tests only when the user explicitly approves fixtures;
- screenshot/log evidence requested;
- expected final verdict format: GREEN / YELLOW / RED.

## 11. Stop conditions

Do not stop merely because one workstream is complete. Continue automatically to the next useful dependency-safe workstream.

Stop only when:

1. the branch contains as much coherent implementation as can safely be completed in the available run;
2. a genuine external prerequisite blocks further meaningful coding;
3. a product choice with materially different user-visible outcomes requires the owner's decision; or
4. execution/context limits require the Light/next-Pro handoff.

When you stop, return a concise implementation summary, not just a plan. The next model should be able to begin QA directly from the branch without reconstructing your reasoning.
