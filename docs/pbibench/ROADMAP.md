# PbiBench roadmap — focused TE2++++++

## v0.1 — clean foundation (current)

- preserve exact upstream TE2 tree;
- add neutral PbiBench.Core;
- PBIP project discovery/context contract;
- Git state parsing primitives;
- DAX formatter capability/privacy contract;
- smoke harness + CI;
- architecture/upstream-sync documentation.

No Fabric/MCP code.

## v0.2 — Model + DAX host

Port/adapt the proven model/DAX ideas from `powerbi_enhanced_dev` without importing its platform scope:

- PbiBench top-level navigation/shell hooks kept minimal in upstream files;
- Semantic View based on TE2 model/dependency data;
- Quick Open / object navigator;
- stronger model-aware DAX editor/workbench;
- DAX query documents/results/history/cancellation where safe;
- expose existing SQLBI DAX Formatter with explicit remote/privacy status;
- investigate an offline formatter only if a compatible tested implementation exists;
- keep `Analyze in DAX Studio` as the deep-performance bridge.

## v0.3 — C# automation

Reuse the strongest old PbiBench automation work:

- multi-tab `.cs/.csx` workspace;
- recovery + content-hash conflict protection;
- TE2 compiler diagnostics + Problems navigation;
- Safe Recipe / Safe Preview lane;
- Trusted C# lane with explicit trust and no sandbox claim;
- recorder -> recipe/generated C#;
- macro library;
- curated Power BI C# Gallery (native/Safe first, TrustedDraft when necessary).

Do not add debugger/projects/NuGet/MSBuild.

## v0.4 — PBIP / TMDL / Git project engineering

- first-class PBIP project context;
- Disk / Loaded / Live / Git / Baseline state;
- semantic Git diff;
- external-change detection;
- reviewed disk/live synchronization;
- recovery snapshots;
- provider-neutral `pbibench-project-context.json` export;
- VS Code handoff.

Do not become a full Git client.

## v0.5 — REPORT / PBIR / theme / lineage

Port the safe report-engineering foundation:

- PBIR report index/tree/wireframe/inspector;
- schema/version checks;
- semantic <-> report lineage;
- exact ReportChangePlan diff/review;
- durable backup/atomic writes/stale detection/restore;
- bounded report actions;
- theme JSON validation/application;
- Theme Forge Design Exchange (`pbibench-model-context.json`, `dashboard-spec.json`, `theme.json`);
- future visual calculations belong to REPORT, not the TE2 semantic model tree.

Keep unknown schemas read-only.

## v0.6 — Explore + Optimize

- bounded table preview;
- typed filters/sorts;
- profile/distribution helpers;
- relationship coverage;
- Pivot Lab;
- BPA packs/preferences;
- VPAX/DMV/model-statistics evidence;
- semantic assertions.

## v1.0 gate

Before calling the product stable, run a real Power BI integration matrix:

- real PBIP project;
- real Power BI Desktop model;
- DAX queries/cancellation;
- model changes + undo + save/reopen;
- Git/TMDL external-change flow;
- PBIR actions + Desktop reopen;
- theme apply + Desktop reopen;
- DAX Studio handoff.

Fabric tenant acceptance is not part of the PbiBench v1.0 gate; it belongs to Fabric Toolbox.
