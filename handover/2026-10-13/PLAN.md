# TE2 Enhanced — Opus pass plan (2026-10-13)

Branch `claude/pbibench-opus-2026-10-13`, based on local `43409e0` (4 unpushed 2026-09-14 Pro commits on top of remote `9fc048e`). Integration branch only: no merge to `master`, no release.

Goal: advance the product from the 2026-09-14 READY_FOR_LIGHT_QA checkpoint to a usable V1 core, in vertical slices that each build and pass both smoke lanes. Done condition per slice: `Scripts/Verify-PbiBench.ps1` exits 0 (7 stages) in the slice worktree, new Core/Host smokes exist for the new logic, slice note `handover/2026-10-13/slices/S#.md` lists implemented vs left for Light QA.

## Facts shaping the plan
- Host code is compiled via `Directory.Build.targets` glob `TabularEditor/PbiBench/**/*.cs` — never edit `TabularEditor.csproj`. Avoid new EmbeddedResource items (keep data as C# constants).
- Menu: `PbiBenchShortcutFilter` is partial; each area implements `AddDaxItems / AddDaxWorkspaceItems / AddAutomationItems / AddProjectItems / AddReportItems / AddExploreItems` in its own `TabularEditor/PbiBench/Navigation/PbiBenchMenu.<Area>.cs`, using `PbiBenchShortcutFilter.Item(text, controller => ...)`.
- Commands: put each area's UIController commands in `TabularEditor/PbiBench/Navigation/UIController.PbiBench.<Area>.cs` (partial `UIController`), not in `UIController.PbiBench.cs`.
- Smokes: `PbiBench.Host.Smoke/SliceSmokes.cs` partials `Early<Area>(TabularModelHandler)` (runs before fixtures that dispose the singleton handler) and `Late<Area>()`; `PbiBench.Core.Smoke/SliceSmokes.cs` partial `<Area>(string fixtureRoot)`. Implement in `SliceSmokes.<Area>.cs`. Areas: Dax, DaxWorkspace, Automation, Project, Report, Explore.
- Core (netstandard2.0, LangVersion 10) stays neutral: no WinForms/TOM/AMO/HTTP/auth.
- Reusable seams: BPA `Analyzer.GetEffectiveRules(..., additionalRules)` (`TabularEditor/BestPracticeAnalyzer/Analyzer.cs`), `ScriptEngine.CompileScript` (`TabularEditor/Scripting/ScriptEngine.cs`).
- Writes: preview → stale check → apply in one undo batch or journaled file plan → restore. Arbitrary C# is trusted, never called sandboxed. OBS-001 stays open: no retry/sleep in save paths.

## Slices
| # | Slice | IDs | Wave |
|---|---|---|---|
| S1 | CI (GitLab local runner + GitHub) and seams | FND/REL-001 | 0 (done in lead) |
| S2 | DAX language service + model usage | DAX-010/011, MODEL-002 | A |
| S4 | Script workspace + broader recipes | AUTO-001/002 | A |
| S5 | Project identity, external change, VS Code, reviewed file plan | PROJ-002/003/004 | A |
| S3 | DAX multi-document, recovery, DAX Studio handoff | DAX-013/014 | B (after S2, uses S4 RecoveryStore) |
| S6 | PBIR index, lineage, reviewed theme apply | REPORT-001/002 | B (uses S5 ReviewedFilePlan, S2 Usage tab) |
| S7 | Bounded profiling, curated BPA/assertions, MCP companion | EXP-002, OPT-001, MCP | B |

### S2 DAX language and model usage
Core new: `Dax/DaxSignatureCatalog.cs` (curated catalog over TE2's pinned function vocabulary, parameter lists written fresh), `Dax/DaxCompletionContext.cs` (caret context: inside `'Table'[`, after VAR, inside string/comment, after function opener), `Dax/DaxDiagnostics.cs` (advisory: unknown object reference, unbalanced delimiters, undeclared variable, arity outside catalog range), `Semantic/SemanticUsageIndex.cs` (measure→referrers, unused measures, unresolved refs from host name sets). Modify `Dax/DaxAuthoring.cs`.
Host new: `Dax/PbiBenchDaxModelBinder.cs` (fresh name sets + model revision stamp, UDFs), `Dax/PbiBenchExpressionEditorAuthoring.cs` (opt-in completion/signature for TE2 expression editor via events, no editor replacement), `Semantic/PbiBenchSemanticViewForm.Usage.cs` (Usage / Unresolved tab), `Navigation/PbiBenchMenu.Dax.cs`. Modify Workbench `.Authoring.cs`, `PbiBenchDaxAuthoring.cs`, `PbiBenchSemanticSnapshot.cs`.
Smokes: Core `DaxCompletionContextSmoke`, `DaxSignatureCatalogSmoke`, `DaxDiagnosticsCorpusSmoke` (quoted names, `''` escapes, VAR/RETURN, nested comments, UDFs; valid + invalid), `SemanticUsageSmoke`; Host `ExpressionAuthoringSmoke` (insert/undo/stale after rename-delete), `UnresolvedReferenceSmoke`.
Accept: correct completion across corpus, signature for every catalog function, diagnostics labelled advisory, no stale items after rename/delete, Usage tab navigates, no network.

### S4 Automation
Core new: `Documents/RecoveryStore.cs` (opt-in, disclosed `%LOCALAPPDATA%\TE2Enhanced\recovery`, retention N days/M files, explicit delete, atomic temp write, hash manifest; commit early — S3 reuses it), `Automation/ScriptDocument.cs`, `Automation/ScriptDiagnostic.cs`, `Automation/RecipeCatalog.cs`. Generalize `Automation/MetadataRecipe.cs` to properties DisplayFolder, IsHidden, FormatString, Description, Rename with typed validators; rename refused when dependents unresolved, otherwise preview dependent expressions TE2 FormulaFixup will rewrite; per-property diff.
Host new: `Automation/PbiBenchScriptWorkspaceForm.cs` (tabs, Problems list from compiler results, explicit per-document trust confirmation "runs with your permissions; not sandboxed", execution through existing ScriptEngine in one undo batch), `Automation/PbiBenchScriptCompiler.cs`, `Navigation/PbiBenchMenu.Automation.cs`, `Navigation/UIController.PbiBench.Automation.cs`. Modify `PbiBenchMetadataRecipe.cs`, `PbiBenchRecipeForm.cs`.
Smokes: Core `RecipeCatalogSmoke`, `ScriptDocumentSmoke`, `RecoveryStoreSmoke`; Host `ScriptWorkspaceSmoke`, `RecipeRenameSmoke`, `RecipeFormatStringSmoke`.

### S5 Project
Core new: `Project/ProjectIdentity.cs` (normalized root + .pbip hash; per-user `%LOCALAPPDATA%\TE2Enhanced\projects.json`, never in repo), `Project/StateIdentity.cs` (Disk/Loaded/Live/Git/Baseline stamps as distinct types), `Project/ExternalChangeDetector.cs` (pure manifest diff, rename heuristic by hash), `Project/ReviewedFilePlan.cs` (multi-file plan with exact before-hash; apply = backup → temp write → replace → journal; restore from journal; stale rejection; commit early — S6 reuses it), `Project/VsCodeHandoff.cs` (argument builder).
Host new: `Project/PbiBenchProjectWatcher.cs` (debounced FileSystemWatcher + rescan on overflow), `Project/PbiBenchSyncReviewForm.cs`, `Project/PbiBenchVsCodeLauncher.cs` (code via PATH/registry, argument list, no shell), `Navigation/PbiBenchMenu.Project.cs`, `Navigation/UIController.PbiBench.Project.cs`. Modify `PbiBenchProjectForm.cs` (identity, change banner, Open in VS Code, Review sync).
Smokes: Core `ProjectIdentitySmoke`, `ExternalChangeSmoke`, `ReviewedFilePlanSmoke` (stale reject; failure on file 2/3 restores; journal restore), `VsCodeArgsSmoke`; Host `ProjectWatcherSmoke`, `SaveConflictSmoke` (or documented deferral if TE2 save hook is too invasive).

### S3 DAX workspace (wave B)
Core new: `Documents/DocumentSet.cs`, `Dax/DaxStudioHandoff.cs` (args from server/database label only, never a connection string; fallback save .dax and open). `DaxDocument` gets stable id + generation. Host: `Dax/PbiBenchDaxWorkbenchForm.Tabs.cs` (per-tab S001 guards), `Dax/PbiBenchDaxStudioLauncher.cs`, `Dax/PbiBenchRecoveryPrompt.cs`; formatter responses keyed by document id. Smokes: `DocumentSetSmoke`, `DaxStudioArgsSmoke`, Host `MultiDocumentSmoke`, `RecoverySmoke`, `ExternalChangeSmoke`.

### S6 Report (wave B)
Core: Newtonsoft.Json 13.0.3 pinned (same as host). `Report/PbirSchemaRegistry.cs` (unknown schema read-only), `Report/PbirReportIndex.cs` (bounded), `Report/PbirFieldReference.cs`, `Report/ReportLineage.cs` (resolved/unresolved/ambiguous), `Report/ThemeApplyPlan.cs` (via ReviewedFilePlan, writable versions only). Host: `Report/PbiBenchReportForm.cs`, `Report/PbiBenchThemeApplyForm.cs`, `Navigation/PbiBenchMenu.Report.cs`; "Used in report" counts in Usage tab. Synthetic fixtures written at runtime. Smokes: `PbirIndexSmoke`, `ReportLineageSmoke`, `ThemeApplyPlanSmoke`, Host `ReportFormSmoke`, `ThemeApplySmoke`.

### S7 Explore / Optimize / MCP (wave B)
Core: `Explore/DaxProfileQueryBuilder.cs` (draft-only bounded profiling), `Model/ModelSnapshotV1.cs` + serializer (no connection strings/rows), `Optimize/SemanticAssertion.cs`. Host: `Explore/PbiBenchProfileForm.cs`, `Optimize/PbiBenchCuratedRules.cs` (via GetEffectiveRules, no user rule file writes), `Optimize/PbiBenchAssertionsForm.cs`, `Model/PbiBenchModelSnapshotPublisher.cs` (explicit "Publish read-only snapshot for MCP…"). Separate `TE2Enhanced.Mcp/` net10 exe referencing Core only, not in TabularEditor.sln: JSON-RPC 2.0 stdio, tools get_model_summary/list_tables/list_measures/get_measure/get_selection, read-only, `--self-test`; doc `docs/pbibench/MCP_COMPANION_V1.md`.

## CI
- GitLab private mirror `juliandatapass-group/tabulareditor-j`, runner JULP (tag `local-windows`, shell PowerShell, LocalSystem). `NUGET_PACKAGES` must stay outside the system profile (32-bit MSBuild WOW64 redirection). Max 4 pipelines at once.
- GitHub Actions `pbibench-foundation` / `pbibench-te2-integration` trigger on `claude/pbibench-*`.

## Estimates (assumptions: one Opus coder per slice, existing harness reused, no live endpoints)
S2/S4/S5 each 5–8 h, 400–700k tokens; S3 5–8 h; S6 5–9 h; S7 6–10 h. Light QA Windows pass afterwards: 4–8 h.
