# PbiBench v0.2 — Model + DAX host

This document tracks the implemented v0.2 foundation separately from the broader roadmap.

## Implemented in this pass

### Semantic Quick Open

- provider-neutral search/ranking remains in `PbiBench.Core/Navigation`;
- the TE2 adapter builds a fresh read-only index from the loaded TOMWrapper object graph;
- `Ctrl+P` opens a lightweight Quick Open dialog;
- filters such as `kind:measure`, `kind:table`, `kind:column`, `kind:fn`, and `kind:ci` are supported;
- hidden objects remain searchable but are ranked below visible equivalents;
- selecting a result delegates to TE2's existing `UIController.Goto(...)`, preserving upstream tree/filter/hidden-object reveal behavior;
- no semantic-model mutation is performed by indexing/search.

The first host implementation intentionally rebuilds the index when Quick Open is opened. This avoids stale-object bugs while object-change invalidation/caching is still being designed. Optimize only after profiling real large models.

### DAX privacy boundary

`DaxFormatterRequestPolicy` defines a privacy-preserving default for PbiBench integrations:

- remote formatting disabled by default;
- DAX text transmission requires explicit enablement;
- model telemetry is a separate opt-in;
- enabling remote formatting alone must not imply telemetry consent.

S001 enforces a shared deny-by-default gate in the actual single/batch proxy before payload creation or redirect discovery. Workbench session consent also governs inherited expression and script/CLI requests. No model telemetry is collected. The serialized payload includes DAX, separator/length/spacing options and app/version fields. Restart revokes consent.

### DAX query/workbench contracts

Core contains provider-neutral contracts used by the dedicated AMO execution adapter:

- bounded row requests (`5000` default, `100000` hard contract maximum);
- bounded timeout (`120s` default, maximum `3600s`);
- cancellation is mandatory on the executor interface;
- result state distinguishes completed, failed and cancelled executions;
- in-memory bounded query history does not persist DAX to disk implicitly;
- consecutive identical queries collapse to the newest history entry.

The dedicated AMO adapter and Workbench are implemented, with local UTF-8 documents, selection execution, history, retained-result budgets and explicit CSV export. Controlled sessions test production orchestration. This is not live Desktop/XMLA certification; real authentication, cancellation and editor-session continuity remain required QA gates. See [S001 developer handoff](../../projectmanagement/handoffs/S001-DEVELOPER.md).

## Upstream-sync strategy

PbiBench application code lives under `TabularEditor/PbiBench/**`.

A root `Directory.Build.targets` injects those sources and the `PbiBench.Core` project reference only when `MSBuildProjectName == TabularEditor`. This avoids editing the large upstream `TabularEditor.csproj` solely to register PbiBench files and reduces future TE2 merge conflicts.

Shell integration uses partial classes. S001 makes authorized formatter-boundary, scripting and expression-handler edits; see the [upstream ledger](UPSTREAM_TE2_SYNC.md).

## Verification

Two CI lanes are expected on v0.2:

1. `PbiBench foundation` — build `PbiBench.Core` and run dependency-free smoke checks.
2. `PbiBench TE2 integration` — restore the legacy TE2 solution and compile the actual WinForms application with the PbiBench host additions.

## Remaining gates and later work

Semantic View, a plain-text Workbench, query adapter and formatter consent are
implemented. Independent QA and lead review have not accepted them. First verify
real Desktop/XMLA, native keyboard/DPI and supported Core/CI checks. Rich language
services, multiple result sets, autosave and recovery remain later work.

The retained estimate, provider-timeout limits, CSV format and concurrent document
save limitations are documented in [README](../../README_PBIBENCH.md).