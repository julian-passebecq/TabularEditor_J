# PbiBench / TabularEditor_J audit — 8 September 2026

Historical audit of `a7317e7`. Subsequent implementation is recorded in [the stabilization update](D:/PROJ/TabularEditor_J/docs/pbibench/STABILIZATION_2026-09-08.md); the findings below describe the original audited source.

## Assessment

The direction is sound: evolve the open-source TE2 application into a stronger model-engineering tool. The current implementation is **the substantial TE2 foundation plus a small, isolated PbiBench extension layer**. It is an early integration branch, not yet a dependable seven-workspace product or a near-TE3 DAX IDE.

The repository is considerably less tangled than the product history and documentation suggest. The product branch adds 33 files / 3,920 lines over upstream, across ten commits, with **no modifications to pre-existing upstream files**. Preserve that manageable foundation. The earlier broad studio contains useful candidate services; it should remain a source for selective ports.

Two concrete gates currently fail: the application does not compile, and two new dialogs throw during construction when tested independently. Fix these before expanding the product surface.

## Scope and evidence

The user's request governs this audit: focus on improving open-source TE2. The uploaded handoff ZIP and pasted TE3 comparison were treated as claims and proposals to verify, not as instructions to execute. Their startup prompt, prior roadmaps, and prohibitions do not independently authorize or constrain implementation.

Reviewed: both repository states, remote branch heads, GitHub CI jobs/annotations, all new feature areas, relevant TE2 services, and selected services in the older local studio. Performed Core compilation, existing smoke execution through an alternate harness, full-host compilation, and isolated .NET Framework dialog-construction probes. No live Power BI Desktop/XMLA query or visual interaction acceptance was performed. The older studio's full test suite was not run.

| Repository / branch | Verified state | Meaning |
|---|---|---|
| `D:\PROJ\TabularEditor_J`, checked-out `master` | `7029129aa3f45d35f987d8f6ac7e5a971f28771c` | Exactly matches origin/master and upstream/master, including live remote checks |
| `origin/pbi-workflow-pro-v0.2` | `a7317e7ff1bbdb8949b7e6d82b6277b6e22300de` | Current product source; remote has not advanced beyond the handoff |
| `origin/pbi-workflow-pro-v0.1` | `b717ba8` | Earlier foundation checkpoint |
| `D:\PROJ\powerbi_enhanced_dev`, `main` | `d75b7ab9df5bd5289a205390111237481cb4e84e` | Earlier studio, matching the port-map reference; clean when inspected |

An isolated detached worktree at `D:\PROJ\TabularEditor_J-audit-v02` holds the audited product source and audit probes. The original checkout remains on master. No product source was changed, committed, pushed, or merged during this audit.

## What exists in the seven proposed areas

These areas are a useful product map. Today, PbiBench adds a `Tools > PbiBench` menu with three dialogs; it does not implement seven top-level workspaces.

| Area | Inherited TE2 capabilities | Actual PbiBench addition | Remaining gap |
|---|---|---|---|
| **MODEL** | TOM tree/property editing; measures, columns, tables, calculation groups, relationships, roles, perspectives, translations, partitions, data sources; dependency tracking, rename fixup, undo; UDF/model metadata support | Ranked Quick Open; semantic snapshot and dependency graph; Semantic View dialog | Repair Semantic View startup; improve navigation/UX; later interactive relationship diagram and report usage |
| **DAX** | Expression syntax highlighting, lexer/token utilities, expression editing, dependency references, SQLBI single/batch formatting, UDF formatting handling | Query request/result contracts, memory history, plain-text Workbench, AMO adapter, row cap, CSV export | Build/startup failures; proper editor completion/signatures/diagnostics/navigation; selected execution; multiple rowsets; document files and recovery |
| **EXPLORE** | Live model connectivity and script-level query helpers | No dedicated preview/profile/Pivot feature in this branch | Bounded table preview first; then profiling, distributions and Pivot |
| **AUTOMATE** | C# editor/completion, compiler diagnostics, selection execution, model-change batching/undo, custom actions/macros, scripting API and CLI | No new automation workspace or preview service | Reviewed change preview/apply, script documents/recovery, recorder/gallery improvements |
| **OPTIMIZE** | BPA engine/UI, rule severity, ignore/suppression machinery, fix expressions, CLI analysis | No new optimization UI/service | Curated rule packs and clearer evidence/fixes; later VertiPaq metrics and usage context |
| **REPORT** | PBIP model resolution understands report references; no full report editor | Discovery recognizes report folders/definition markers only | PBIR page/visual parsing, report lineage, themes and diagnostics; reviewed edits later |
| **PROJECT** | PBIP semantic-model resolution; TMDL load/save; BIM/folder serialization | Bounded directory discovery, project-context JSON serializer, minimal Git-output parsers | These new utilities have no host caller; need visible project context, actual Git status, disk/model comparison and conflict handling |

Evidence anchors: [TE2 PBIP/TMDL handling](D:/PROJ/TabularEditor_J-audit-v02/TOMWrapper/TOMWrapper/TabularModelHandler.FileHandling.cs:18), [C# execution and undo](D:/PROJ/TabularEditor_J-audit-v02/TabularEditor/UI/UIController_ScriptEditor.cs:101), [BPA rule facilities](D:/PROJ/TabularEditor_J-audit-v02/TabularEditor/BestPracticeAnalyzer/BestPracticeRule.cs:139), [PbiBench command registration](D:/PROJ/TabularEditor_J-audit-v02/TabularEditor/PbiBench/Navigation/PbiBenchShortcutFilter.cs:62).

## Confirmed defects and integration gaps

### 1. Application build fails — release blocker

The current GitHub job and local build both report `CS0104`: `Server` is ambiguous between `Microsoft.AnalysisServices.Server` and `Microsoft.AnalysisServices.Tabular.Server`.

[Adapter source](D:/PROJ/TabularEditor_J-audit-v02/TabularEditor/PbiBench/Dax/PbiBenchAmoDaxQueryExecutor.cs:216) imports both namespaces and uses an unqualified `Server` parameter and construction. Start by choosing/aliasing the intended server type and rebuilding against pinned Analysis Services `19.112.0`. Additional diagnostics may emerge after this first compiler error is resolved; a one-line fix is not yet proof of a clean build.

This corrects the handoff's unverified suggestion that the recorded failure was an AMO method-signature incompatibility. There is no demonstrated reason from this error alone to replace the query stack or add another transport dependency.

### 2. DAX Workbench and Semantic View fail to construct — release blockers

Both fail with:

`InvalidOperationException: SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize.`

The dialogs set splitter distances/minimum panel sizes before the new split containers have enough size. `Dock = Fill` does not size an unparented control. This was reproduced using the actual unchanged form sources on **.NET Framework 4.8**, with an offline model for Semantic View.

- [DAX layout initializer](D:/PROJ/TabularEditor_J-audit-v02/TabularEditor/PbiBench/Dax/PbiBenchDaxWorkbenchForm.cs:104): first split fails; also review the second initializer at line 124.
- [Semantic View initializer](D:/PROJ/TabularEditor_J-audit-v02/TabularEditor/PbiBench/Semantic/PbiBenchSemanticViewForm.cs:49): same ordering problem.
- Quick Open's constructor passes the same probe. Navigation, focus and interaction still need acceptance testing.

Size/layout initialization must be corrected and verified across minimum window size and DPI settings. Semantic View's previous CI success proves compilation only; its handoff status must be downgraded.

### 3. The Workbench is a query prototype, not a rich DAX editor

The editor is a plain `RichTextBox`. Execution reads the entire `.Text`, not the selection. The result contract exposes one columns/rows pair, and the adapter never advances to another result set. The grid is virtualized but all returned rows are still materialized in memory. A row cap does not cap server workload or bytes held by wide/string-heavy results.

History is limited to 100 entries and owned by the modal form, so it disappears when the dialog closes. It stores a success boolean; cancelled/time-out queries appear as "Failed" in the history list. There is no new `.dax` document open/save/recovery workflow. CSV export exists in source but has not had end-to-end acceptance.

### 4. Cancellation and diagnostics need runtime hardening

The adapter uses a dedicated server object/session, which is a useful separation from TE2's editing connection. However, it registers transport cancellation only **after** synchronous `Connect` returns. The requested timer cannot interrupt a stalled connection through that registration. Query cancellation calls synchronous `CancelCommand`; cancellation initiated on the UI thread therefore needs responsiveness testing. Engine cancellation is best effort, not a demonstrated wall-clock deadline.

`SafeErrorMessage` only trims/truncates exception text. It does not redact sensitive content. The form's fallback exception path displays the raw message without that cap. These are source-level findings; timeout/cancel behaviour and authentication cloning have not been tested against Desktop or a service endpoint.

### 5. Formatter policy is not connected to formatting

`DaxFormatterRequestPolicy` is referenced by Core smoke checks, not the host's actual formatting paths. The inherited formatter already has a proxy abstraction and single/multi-expression APIs, so batch support should not be planned as wholly new work.

TE2 already supports an administrative disable policy and a telemetry preference. `ModelTelemetry.Collect()` respects `Preferences.Current.CollectTelemetry`, whose declared default is true. Request construction includes DAX, app/version metadata and, when enabled, hashed server/database identifiers plus environment metadata. The new policy's separate remote-formatting/telemetry decisions do not currently govern these calls.

Integrate the existing formatting paths into one explicit policy/service boundary, audit the actual payload, and decide whether migrating to SQLBI's client improves maintenance. A client package's license does not itself answer what a remote service receives.

Evidence: [formatter implementation](D:/PROJ/TabularEditor_J-audit-v02/TabularEditor/UIServices/DaxFormatter.cs:58), [telemetry preference](D:/PROJ/TabularEditor_J-audit-v02/TabularEditor/UIServices/Preferences.cs:24), [collection gate](D:/PROJ/TabularEditor_J-audit-v02/TabularEditor/UIServices/ModelTelemetry.cs:93), [batch formatting](D:/PROJ/TabularEditor_J-audit-v02/TabularEditor/Scripting/ScriptHelper.cs:355).

### 6. Project utilities are a foundation, not a workspace implementation

Search found no host references to `PbipProjectDiscovery`, `ProjectContextSerializer` or `GitStatusParser`. The Git parser only classifies nonempty output, parses branch text and validates a SHA-like string; it does not run Git or enumerate changes.

Discovery infers artifacts from folder suffixes and markers rather than parsing PBIP links. `FindPbip` selects the alphabetically first project in a directory, which needs attention before supporting multiple PBIPs. The directory walk is bounded, but `ContainsTmdlDefinition` uses recursive enumeration without that walk's depth/reparse exclusions. Do not advertise the whole scan as uniformly bounded until this is reconciled.

### 7. Validation currently misses user-visible failures

The Core suite covers discovery, JSON round trips, basic Git parsing, formatter policy values, query bounds/history, Quick Open matching and graph traversal. It does not construct dialogs, exercise the AMO adapter, or prove live query/cancellation/truncation. The current suite has no assertions for `IsTruncated` / `ReturnedRowCount` specifically.

Host CI compiles but does not run UI smoke checks or the existing TE2 regression suites. Its path filters also omit many upstream host/TOM/ANTLR/package inputs. Changes to those inputs alone can escape the PbiBench integration job. Expand gates when stabilizing the branch, rather than treating Core green as product green.

## Value of the earlier studio

The previous work is still present locally. These are **source-inspected port candidates**, not accepted features of this branch and not proof of production readiness.

| Candidate | Inspected source / test evidence | Port judgment |
|---|---|---|
| DAX language service | `src/PbiBench.Dax.LanguageService`: tokenizer, metadata-based completion, signatures, diagnostics, navigation, model scripts and code actions; dedicated tests | Highest-value candidate after stabilization. Reconcile with TE2's lexer/dependencies and choose one owner per language function |
| Safe script preview | `src/PbiBench.Semantic/ModelAuthoring/ScriptPreviewService.cs`; `ScriptPreviewTests.cs` | Detached metadata execution for a restricted interpreted recipe/C# subset, fingerprints and reviewed deltas; useful foundation |
| Trusted C# | `src/PbiBench.ModelEditor/TrustedScriptRunner.cs`; boundary tests | Uses TE2 compiler, pre-run snapshot and live execution. This is not arbitrary-C# detached preview or a sandbox |
| Explore | `src/PbiBench.Core/DataExploration`: preview, profiling and Pivot contracts/builders; adapter tests | Extract query/analysis services first; defer old WPF views |
| PBIR lineage | `src/PbiBench.Pbir/ReportLineage.cs`, PBIR services and tests | Start with metadata-only indexing and unresolved-reference reporting; validate against real PBIR fixtures |
| Workspace / Git | `src/PbiBench.Workspace`, `src/PbiBench.Git`, semantic workspace sync | Reuse small scanner, status, snapshot and diff services with explicit current-model integration |
| Packages / recording | `DaxPackageService.cs`, `ActionRecorder.cs` and supporting contracts | Later candidates; packaging/compatibility and replay safety need review |

Some old libraries already target `net48` alongside `net10.0`. However, the Semantic project references a vendored TE2 build path and other old projects. This is not a drop-in reference migration. Its WPF application, Fabric/auth services and agent surfaces should not be imported wholesale into the TE2 host.

The distinction between restricted Safe Preview and unrestricted Trusted C# is particularly important: the pasted proposal overstates what has already been implemented if read as preview support for any C# script.

## Revised development order

### Milestone 1 — A dependable TE2-based integration branch

1. Fix the `Server` ambiguity and any subsequently exposed compiler errors.
2. Fix both dialog layouts; retain constructor smoke coverage on net48.
3. Run Core checks and relevant TE2 regression tests; include actual upstream dependencies in host CI triggers.
4. Accept Quick Open and Semantic View interaction on representative small/large models.
5. Accept the Workbench against Desktop/XMLA: success, errors, cancellation, connection timeout, truncation, CSV and preservation of the editor connection.
6. Make the product branch/build identity obvious; keep master as the clean upstream baseline if that remains the chosen branching model.

Exit criterion: a reproducible build whose advertised features open and whose query lifecycle is demonstrated. Avoid a broad shell redesign during this milestone.

### Milestone 2 — DAX authoring quality

Use one editor/service design for expressions and queries. Reuse TE2 highlighting/navigation/dependency services, and evaluate a selective port of the earlier language service for missing completion/signature/diagnostic capabilities.

Deliver selected-text execution, function and model-object completion, signatures, bracket handling/folding, definition navigation, a Problems surface, formatting through the real policy boundary, and `.dax` documents with unsaved-change handling. Add multi-result-set support and clear Completed/Failed/Cancelled history. Prove behaviour using a corpus of valid/invalid DAX, quoted identifiers, variables and UDFs.

The public [TE3 DAX editor documentation](https://docs.tabulareditor.com/en/features/dax-editor.html) is a useful observable capability benchmark for completion, parameter help, navigation and consistent expression/query/script editing. This audit does not claim version parity or certify every latest-release claim in the pasted text.

### Milestone 3 — Automation and project workflow

Port the restricted preview/diff/apply service and adapt it to current TOMWrapper undo and stale-model checks. Improve trusted script documents, recovery and diagnostics separately. Wire project discovery into a small visible Project panel, then actual Git status, disk/model comparisons and conflict-aware reload/save.

### Milestone 4 — Explore and report differentiation

Add bounded preview/profile functionality, then PBIR read-only pages/visuals and measure usage. Use report coverage and unresolved-reference evidence before recommending object removal or rename. Diagrams and VertiPaq metrics can follow these foundations; Pivot and package management can be scheduled as independent, bounded slices.

Keep advanced debugger/optimizer and arbitrary Power Query schema analysis later. The pasted examples such as replacing `IFERROR` with `DIVIDE` or rewriting `FILTER` are not universal equivalences: any automated rewrite needs explicit applicability rules and semantic tests.

## Validation record

| Check | Result |
|---|---|
| Remote heads and branch diff | Verified live; 33 added files, zero modified upstream files |
| Core Release build | Passed locally, zero warnings/errors |
| Repository smoke command | Local environment blocked: target net10.0, installed SDK 9.0.101 |
| Existing smoke sources in temporary net9 harness | Passed; one nullable warning in existing semantic smoke source |
| GitHub Core run at audited head | [Passed](https://github.com/julian-passebecq/TabularEditor_J/actions/runs/34064620720) |
| ANTLR Debug generation | Passed locally |
| Full net48 host build | Failed locally with CS0104; matches [current CI failure](https://github.com/julian-passebecq/TabularEditor_J/actions/runs/34064620738) |
| Earlier Semantic View host build | [Passed at d004584](https://github.com/julian-passebecq/TabularEditor_J/actions/runs/34064245422); not a runtime result |
| Isolated net48 UI probe build | Passed, zero warnings/errors |
| Quick Open constructor | Passed |
| DAX Workbench constructor | Failed: invalid split-panel layout |
| Semantic View constructor with offline TOM model | Failed: invalid split-panel layout |
| Full UI interaction / live query / existing TE2 test suites | Not run |

Reproduction assets remain in [audit probes](D:/PROJ/TabularEditor_J-audit-v02/audit-probes/ui/Program.cs), [net48 UI project](D:/PROJ/TabularEditor_J-audit-v02/audit-probes/ui/ui.csproj), [alternate smoke harness](D:/PROJ/TabularEditor_J-audit-v02/audit-probes/smoke/smoke.csproj), and [host build log](D:/PROJ/TabularEditor_J-audit-v02/audit-host.log).

Local host compilation required explicitly locating Visual Studio Build Tools, setting `MSBuildSDKsPath` to `C:\Program Files\dotnet\sdk\9.0.101\Sdks` and disabling workload resolution for that command. These were process-local environment adjustments, not repository changes. Legacy packages were restored with solution-level MSBuild `/t:Restore /p:RestorePackagesConfig=true`; ANTLR Debug output was generated before the Release host build.

## Corrections to carry into future planning

- Keep the handoff's branch history and isolated Core/host architecture: these check out.
- Replace its vague build diagnosis with the confirmed CS0104 error.
- Downgrade Semantic View from validated to runtime-broken; DAX Workbench has both compile and construction blockers.
- Count inherited model editing, PBIP/TMDL, batching, BPA severity/fixes and C# facilities as existing assets.
- Describe project context and formatting policy as unwired foundations.
- Treat old studio features as port candidates until adapted and accepted here.
- Describe the seven areas as the product roadmap, not current screens.
- Preserve TE2 and third-party attribution from the repository's MIT and bundled license notices; develop new capabilities from open-source components and independently implemented behaviour.

The immediate objective is a reliable, recognizably TE2 application with excellent DAX authoring. That gives subsequent automation, project and report features a stable place to live.
