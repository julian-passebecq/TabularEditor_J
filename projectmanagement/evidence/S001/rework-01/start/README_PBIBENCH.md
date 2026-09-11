# PbiBench / PBI Workflow Pro

A focused Power BI model-and-report engineering workbench built on the open-source Tabular Editor 2 codebase.

Current delivery plan, architecture decisions, developer/tester handoffs and sprint status: [projectmanagement](projectmanagement/README.md).

This repository intentionally keeps the upstream Tabular Editor 2 tree recognizable so that upstream fixes can be synchronized with low conflict. PbiBench additions live in their own projects/folders and upstream shell edits must stay small and documented.

## Product scope

PbiBench is the **Tabular Editor 2++++++** product:

- semantic model authoring and model navigation;
- DAX authoring/query workflow and formatting;
- data exploration that helps model engineering;
- C# scripting, script workspace, curated Power BI automation gallery;
- BPA / model quality / optimization;
- PBIP + TMDL + Git project workflow;
- PBIR report engineering, themes and semantic-to-report lineage;
- provider-neutral Project Context JSON for external tools and assistants.

## Explicitly out of scope

The following are separate products and must not be pulled into this repository's core application:

- Fabric workspace/admin/OneLake/capacity tooling -> **Fabric Toolbox**;
- MCP/agent configuration and orchestration -> future **MCP Companion**;
- deep DAX timings/query plans -> **DAX Studio**;
- visual theme simulation -> **Theme Forge**;
- final report rendering -> **Power BI Desktop**.

## Upstream baseline

Upstream: `TabularEditor/TabularEditor`

Pinned clean baseline for this work: `7029129aa3f45d35f987d8f6ac7e5a971f28771c` (2026-05-15).

The fork's `master` matched that upstream commit when this work started.

## Branches

Stable foundation checkpoint: `pbi-workflow-pro-v0.1`

Current S001 delivery branch: `codex/stabilize-pbibench-v02` (uncommitted delivery; see STATUS).

### v0.1 foundation

1. scope/update contract;
2. neutral `PbiBench.Core` library;
3. PBIP discovery primitives (root, ambiguity and traversal fixes remain required before Project UI wiring);
4. provider-neutral project-context serialization;
5. Git status parsing primitives without becoming a Git client;
6. explicit DAX formatter capability/privacy metadata;
7. dependency-free smoke harness and CI.

### v0.2 current work

- semantic Quick Open ranking/search in neutral Core;
- `Ctrl+P` loaded-model navigation wired to TE2 through isolated partial classes;
- read-only TOMWrapper semantic indexing;
- shared SQLBI formatter gate with explicit process-session consent, administrative override and no model telemetry;
- dedicated AMO query session with asynchronous cancellation and retained-result limits;
- bounded memory-only query history;
- dedicated TE2 integration build lane.

See `docs/pbibench/ARCHITECTURE.md`, `docs/pbibench/ROADMAP.md`, and `docs/pbibench/V0_2_MODEL_DAX.md`.

## S001 implementation and verification limits

The DAX Workbench supports local UTF-8 drafts, selection execution (F5/Ctrl+Enter),
one result set, transient history and explicit CSV export. Query defaults: 5,000
rows, 120 seconds requested timeout, 32 MiB retained estimate, 256 columns, 1 MiB
per retained string/binary cell. The result grid, history and export status identify
incomplete results. Limits preserve accepted values; they do not bound server
work, AMO buffering or the time to receive one provider value.

Cancellation signals a dedicated session on a worker. The draft stays open and
new execution/document replacement/export remain blocked until provider cleanup
finishes. Native connection/command timeout settings are applied, but a stuck
provider/authentication/cleanup call is not a proven hard wall-clock deadline.
Force exit or OS shutdown can still lose an unsaved draft; there is no autosave.

Use **Remote formatting** in the Workbench to enable or revoke session consent.
This also governs inherited expression F6/Ctrl+F6 and script/CLI formatting.
Restart disables consent. SQLBI requests contain DAX, formatting options and
app/version fields; no model telemetry is collected, even when the inherited
telemetry preference is enabled. Workbench F6 formats the entire draft as one
undoable edit; edits/cancellation/close/revocation discard pending responses.
Revocation cannot retract a request already sent.

CSV uses comma-separated, quoted fields, doubled quotes, preserved embedded
newlines, UTF-8 BOM, invariant numbers, and round-trip ISO date/time values.
Null is empty; binary is Base64. Formula-like text is preserved as data (no
spreadsheet formula sanitization). A sibling temporary file is replaced/moved
only after successful writing. This preserves an existing destination on ordinary
write failure; it is not power-loss durability or concurrent-editor exclusion.

Run `./Scripts/Verify-PbiBench.ps1` with VS MSBuild, NuGet, .NET Framework 4.8
reference assemblies and .NET 10 SDK. `-NuGetPath` supports an explicit tool path.
`-HostOnlyDiagnostic` runs the host lane and explicitly excludes canonical Core
smoke; it cannot certify the full sprint. See the
[developer handoff](projectmanagement/handoffs/S001-DEVELOPER.md) for evidence.
Independent QA, real Desktop/XMLA/authentication, native DPI interaction and lead
acceptance remain separate gates. The Project/report features above are product
direction, not a claim of delivered end-to-end workflows.