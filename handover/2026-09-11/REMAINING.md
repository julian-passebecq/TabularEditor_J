# Remaining outcomes for the full app

This is the documented product ambition, not a claim that every planned feature is already authorized for immediate coding. Only S001 had a detailed authorized implementation packet. The successor should prioritize against the owner's final-version scope, retaining honest supported-feature limits. IDs refer to projectmanagement/BACKLOG.md. These are outcomes, not prescribed implementation steps.

| Area | Remaining outcome / completion evidence |
|---|---|
| Current reliability gate | Focused review/decision on corrected BUG-001 numeric fidelity and BUG-002 stale formatting; retain unresolved live/native/build gates. Capture cause of OBS-001 if second-save failure recurs. |
| Build and release (REL-001) | Reproducible clean-checkout canonical net10 Core checks, actual net48 host checks and usable legacy regression lane; remote CI evidence; clear supported environments and distributable release validation. |
| Live execution (DAX-006) | Approved disposable Desktop and XMLA fixtures demonstrate query/error/cancel/timeout behavior, cleanup, editing-session survival and supported authentication. Supported limitations must be explicit. |
| Native usability | Complete document open/save/dirty/close, keyboard/undo and real DPI journey; representative large models; navigation freshness after rename/delete and hidden-object behavior. |
| DAX authoring (010/011) | Consistent model/function completion, signatures, navigation and advisory diagnostics, backed by valid/invalid DAX corpus including quoted names, variables, comments and UDFs. Language ownership/provenance still needs assessment. |
| DAX workspace (012/013/014) | Multiple result sets with independent selection/export and shared bounds; multiple documents, conflicts and explicit recovery/retention; actual DAX Studio handoff. |
| Model (002) | Useful relationship/usage views with unresolved references visible, large-model usability and trustworthy report-usage evidence. |
| Automation (001–003) | Script workspace with documents, diagnostics, recovery and explicit trusted execution; restricted recipe preview/diff/apply with stale rejection, undo and failure semantics; later repeatable macros/gallery. Existing TE2 C# support is not this completed workspace. |
| Project discovery (001) | Correct filesystem roots/UNC, bounded traversal and junction/access-denied handling; explicit selection for multiple PBIPs. Current discovery is not ready for reliable Project UI wiring. |
| Project workflow (002–004) | Read-only project/Git/context view; distinct Disk/Loaded/Live/Git/Baseline identities and external-change detection; reviewed synchronization, conflict handling, recovery and VS Code handoff without silent overwrite. |
| Report (001–003) | Version-aware PBIR index and semantic lineage, unresolved references and unknown schemas clearly handled; reviewed report/theme changes with restore and Desktop reopen evidence; Theme Forge exchange. Presently only folder-discovery foundations exist. |
| Explore (001/002) | Bounded table preview and typed filters/sorts, then profiling/distributions/relationship coverage/Pivot Lab with truthful query costs and stale context handling. |
| Optimize (001/002) | Curated BPA rules/preferences/assertions and reviewed fixes; storage/VPAX/DMV and usage recommendations with provenance, applicability and bounded queries. Inherited BPA alone is not the whole target. |
| Final integration | PBIP/TMDL open/edit/undo/save/reopen; real endpoint matrix; project external-change/recovery; report/theme apply/restore/reopen; specialist-tool handoffs; privacy and large-model interaction evidence. |

Environment/access the owner may need to supply: approved disposable Power BI Desktop model and XMLA endpoint/authentication context; no credentials should be placed in Git. SDK/reference setup and CI investigation remain technical work for the successor. No manual Git push is expected if the publication described in PUBLICATION.md is verified.

Deferred, not current defects: offline formatter, persistent formatter consent and optional telemetry settings. Do not inflate the release promise to include all exploratory ideas without a product decision.
