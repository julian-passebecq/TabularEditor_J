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

The inherited TE2 SQLBI DAX Formatter implementation is still the existing upstream service path. The new policy is a Core contract; the host must enforce it before PbiBench exposes a new formatter entry point.

### DAX query/workbench contracts

Core now contains provider-neutral contracts for future query execution:

- bounded row requests (`5000` default, `100000` hard contract maximum);
- bounded timeout (`120s` default, maximum `3600s`);
- cancellation is mandatory on the executor interface;
- result state distinguishes completed, failed and cancelled executions;
- in-memory bounded query history does not persist DAX to disk implicitly;
- consecutive identical queries collapse to the newest history entry.

There is intentionally **no new DAX execution adapter yet**. A concrete Desktop/XMLA implementation should only be added after connection, cancellation and result-shape behavior are tested against real Power BI Desktop / XMLA endpoints.

## Upstream-sync strategy

PbiBench application code lives under `TabularEditor/PbiBench/**`.

A root `Directory.Build.targets` injects those sources and the `PbiBench.Core` project reference only when `MSBuildProjectName == TabularEditor`. This avoids editing the large upstream `TabularEditor.csproj` solely to register PbiBench files and reduces future TE2 merge conflicts.

No existing `FormMain.cs`, `FormMain.Designer.cs`, `UIController.cs`, or `UIController_Tree.cs` behavior is replaced. Shell integration uses partial classes.

## Verification

Two CI lanes are expected on v0.2:

1. `PbiBench foundation` — build `PbiBench.Core` and run dependency-free smoke checks.
2. `PbiBench TE2 integration` — restore the legacy TE2 solution and compile the actual WinForms application with the PbiBench host additions.

## Next v0.2 slices

1. real-model Quick Open interaction test on Windows with a PBIX/PBIP model;
2. richer Semantic View using TE2 dependency data;
3. model-aware DAX workbench shell with query documents/results;
4. Desktop/XMLA query adapter with cancellation and bounded result materialization;
5. explicit PbiBench formatter consent UI and telemetry-free request path;
6. DAX Studio handoff retained for deep timings/query-plan analysis.
