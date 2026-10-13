# Reuse map from the previous broad PbiBench repository

Reference repository: `julian-passebecq/powerbi_enhanced_dev`

Last audited broad-product HEAD before the scope reset:
`d75b7ab9df5bd5289a205390111237481cb4e84e`

The old repository is a source of proven PbiBench-owned ideas/logic. It is **not** copied wholesale because that would re-introduce Fabric/platform coupling and make TE2 upstream sync harder.

## Reuse / port

| Old capability | New owner | Port strategy |
|---|---|---|
| Semantic View / dependencies / report usage | PbiBench MODEL | adapt to current TE2 model/dependency services |
| DAX Workbench / completion / results / history | PbiBench DAX | port language/query services behind net48 host adapter |
| Quick Open / object index | PbiBench MODEL/DAX | pure index + small WinForms surface |
| Data preview / profile / Pivot Lab | PbiBench EXPLORE | port bounded query/profile services |
| Safe C# / Trusted C# split | PbiBench AUTOMATE | preserve exact trust boundary |
| C# workspace/recovery/problems | PbiBench AUTOMATE | port PbiBench-owned editor logic around TE2 compiler |
| Recorder/macros/gallery | PbiBench AUTOMATE | port; curate rather than copy every script |
| BPA packs / optimization / assertions | PbiBench OPTIMIZE | port PbiBench-owned rule/evidence layers |
| PBIP/TMDL/Git workspace | PbiBench PROJECT | port pure project/diff/recovery services |
| AI Context Export | PbiBench PROJECT | reframe as provider-neutral Project Context Export |
| Report Studio / PBIR engine | PbiBench REPORT | port safe index/change/backup/restore logic |
| semantic-report lineage | PbiBench MODEL + REPORT | port metadata-only cross-layer index |
| theme validation / Design Exchange | PbiBench REPORT/PROJECT | port provider-neutral JSON contracts |
| DAX Studio handoff | PbiBench DAX | keep external bridge only |

## Do not port into this product

| Old capability | Destination |
|---|---|
| Fabric workspace inventory | Fabric Toolbox |
| OneLake browser | Fabric Toolbox |
| Fabric SQL preview | Fabric Toolbox |
| Fabric jobs/schedules/activity | Fabric Toolbox |
| Capacity/admin/governance | Fabric Toolbox |
| MSAL/Fabric REST auth | Fabric Toolbox |
| Fabric report remote definition transport | Fabric Toolbox |
| MCP setup/client/orchestration | future MCP Companion |
| embedded provider/agent UI | do not make core product dependency |

## Porting rule

Before porting any old file:

1. identify the smallest pure service/contract actually needed;
2. remove Fabric/auth/provider references;
3. place it in the correct focused module;
4. add tests before wiring it into FormMain;
5. prefer one minimal shell hook over broad upstream UI edits.
