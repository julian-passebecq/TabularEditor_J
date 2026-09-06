# PbiBench / PBI Workflow Pro

A focused Power BI model-and-report engineering workbench built on the open-source Tabular Editor 2 codebase.

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

Current integration branch: `pbi-workflow-pro-v0.2`

### v0.1 foundation

1. scope/update contract;
2. neutral `PbiBench.Core` library;
3. bounded PBIP project discovery;
4. provider-neutral project-context serialization;
5. Git status parsing primitives without becoming a Git client;
6. explicit DAX formatter capability/privacy metadata;
7. dependency-free smoke harness and CI.

### v0.2 current work

- semantic Quick Open ranking/search in neutral Core;
- `Ctrl+P` loaded-model navigation wired to TE2 through isolated partial classes;
- read-only TOMWrapper semantic indexing;
- remote formatter consent/telemetry policy contract;
- bounded DAX query request/result/executor contracts with cancellation;
- bounded memory-only query history;
- dedicated TE2 integration build lane.

See `docs/pbibench/ARCHITECTURE.md`, `docs/pbibench/ROADMAP.md`, and `docs/pbibench/V0_2_MODEL_DAX.md`.
