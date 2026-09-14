# Light QA handoff — TE2 Enhanced

Role: independent Windows QA, not implementation. Read AGENTS and PRO_IMPLEMENTATION_STATUS first. Do not restart S001 corrections or silently fix production code. Do not merge, push or release. New Pro features are implemented for review, not independently accepted.

## Identity and prerequisites

Test branch `codex/pro-ai-handover-2026-09-11`. Exact delivered product commit is recorded in the final identity section below. Inspect `git status --short --branch` and `git rev-parse HEAD` before testing; preserve any later user work. A docs-only handoff commit may follow the product commit. Rebuild product, not only the test harness.

This machine: Windows, VS Build Tools 17.12, .NET Framework 4.8 reference assemblies, system stable SDK 9.0.101 for VS imports, private SDK 10.0.401 at `.tools/dotnet/dotnet.exe`, Git on PATH. NuGet has been copied to ignored `.tools/nuget.exe`; no dependency on the old application's source or runtime. On another machine install these prerequisites or a newer mutually compatible VS/SDK combination. The private SDK and NuGet binaries are not committed.

Commands from `D:/PROJ/TabularEditor_J`:

```powershell
git status --short --branch
git rev-parse HEAD
dotnet --list-sdks
& ./.tools/dotnet/dotnet.exe --version
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Verify-PbiBench.ps1 -DotNetPath .tools/dotnet/dotnet.exe -NuGetPath .tools/nuget.exe -EvidenceDirectory D:/PROJ/TabularEditor_J/.tools/light-qa
```

The runner restores dependencies, rebuilds ANTLR and the actual Release host, runs canonical net10 Core, builds the net48 host harness and executes it. Exit 0 is required for this lane. `-HostOnlyDiagnostic` does not certify canonical Core. Evidence distinguishes every stage. Do not call a harness-only rebuild a product rebuild.

Executable: `D:/PROJ/TabularEditor_J/TabularEditor/bin/Release/TabularEditor.exe`.
Harness: `D:/PROJ/TabularEditor_J/PbiBench.Host.Smoke/bin/Release/net48/PbiBench.Host.Smoke.exe`.
Synthetic BIM: `projectmanagement/evidence/S001/lead/PbiBench-Demo.bim`. Also use the existing upstream synthetic fixtures where relevant, and create disposable PBIP/report-folder/Git fixtures in an owned temp directory. Never store real rows, private DAX or credentials in test evidence.

## Required independent matrix

| Area | Execute and inspect |
|---|---|
| Ordinary TE2 | Open synthetic BIM; edit/commit expression and properties; undo/redo; save a new copy; reopen and compare. Exercise trusted C# and inherited BPA on synthetic metadata. Confirm ordinary menu/shortcut behavior remains intact. |
| Quick Open / Semantic View | Hidden objects, duplicate labels, rename/delete/reopen freshness. New All table relationships tab: cardinalities, one/both directions, active/inactive, missing endpoint, double-click and Enter navigation. It shows all relationships, not a filter of the left object. General unresolved DAX references/report usage remain unimplemented. |
| Local DAX authoring | Workbench Authoring menu: Ctrl+Space picker, filtering, escaped table/column names, hidden objects and model UDFs where supported; insert at caret/replace selection and Undo. Function vocabulary is pinned TE2, not a live availability list. Go to object. Ctrl+Shift+Space nested arguments; Ctrl+F7 valid/invalid delimiters with strings, nested comments, VAR and UDF syntax. Checks are advisory, not engine certification. Try equal-text replacement while formatting is pending; authoring must not weaken S001 identity guards. |
| Multiple results | Synthetic controlled-reader smoke plus approved live multi-EVALUATE queries later. Zero/one/2/32/33 sets; each selector entry shows distinct data and CSV/copy selects only that set. Duplicate column names remain independent. Test per-set row caps, shared byte exhaustion, cell/column overflow, cancellation between sets, NextResult failure, cleanup/admission and remaining-results-not-read messages. History row count aggregates sets; first-set compatibility remains intentional. |
| DAX documents / formatter | Existing single-document New/Open/Save/Save As, UTF-8/BOM/invalid encodings, dirty prompts, cancellation, failed save, external modify/delete and Save As preservation. Held formatter responses must not cross equal-text document/object/property/model transitions. Session consent/admin-disable/no telemetry and script/CLI entry-point denial. No real SQLBI call unless explicitly consented; controlled transport is the default test. Multiple documents, recovery and DAX Studio handoff are not implemented. |
| Project | Tools > PbiBench > Project (read-only): select folder/PBIP, select among several PBIPs, refresh after modify/delete/rename, root path, worktree `.git` file, deep/wide trees, junction loops and unreadable directories. Verify incomplete diagnostics and no inferred PBIP association. Git clean/dirty/untracked/staged/rename/detached, missing Git and bounded timeout/output errors. Source changes during scan are disabled; close during scan must not update disposed controls. Export context to an explicit new path and inspect for only allowlisted fields. Open folder. No source files or Git index should be rewritten by inventory. |
| Restricted recipes | Choose table, folder or hidden operation. Preview must not mutate. Check exact Before/After and scope, change input invalidates preview, no-op has no Apply, >1,000 targets rejects. Apply and one Undo restore all. Change metadata after preview, replace target with identical name/metadata, switch model or open an existing undo batch: reject. Inject a second-setter failure and confirm first change rolls back and batch depth returns to zero. If rollback itself fails, UI must report failure without pretending atomicity. No arbitrary C# or disk/live save. |
| Table preview | Table/column selection; text with quotes and DAX-looking payload, decimal dot, true/false, blank; sorting and row limits 1/5,000. Opens an unsaved draft and does not execute automatically. Rename/delete/change model before execution must reject. Offline stays draft-only. TOPN ties may expand server output; retained row cap still applies. Unsorted order is unspecified. |
| Native UI | All new dialogs: keyboard-only Tab/Enter/Esc, accelerators, focus return, minimum-size/resize, real 100/150/200% DPI and multi-monitor transition. Check scroll access to Workbench result selector and preview controls. Screenshots of normal/error/truncated/dirty states. Harness handle/layout checks do not certify this journey. |

## Open gates and known observations

- **OBS-001 remains OPEN and recurred in this Pro run.** Document second Save and CSV replacement each once threw `IOException: Unable to remove the file to be replaced` from `File.Replace`; subsequent runs passed. First failures are preserved under `evidence/pro-2026-09-14`. Investigate Windows locking/permissions and capture HRESULT (the harness now reports it), file state and whether destination/draft/results survived. Do not label retries as resolution or silently add retry/sleep to tests. This may warrant a YELLOW verdict even with a passing final lane.
- Full legacy `TabularEditorTest` still depends on absent historical VisualStudio.QualityTools references. It is isolated, not retargeted or included in the runner's passing claim. Build it with VS MSBuild on a provisioned environment and document the result. TOM endpoint-dependent tests must stay BLOCKED_ENV without approved fixtures. Existing historical 8-pass/3-blocked TOM results are not fresh evidence.
- Remote clean-checkout CI is NOT_RUN. Both workflows now include takeover branches; no push/release occurred.
- No live Desktop/XMLA/auth profile has been certified. Request approved disposable fixtures before live queries. Do not attach to unknown local server processes or use discovered credentials. Test editing-session survival, cancellation/timeout/cleanup, multi-results and supported authentication with endpoint/version/source identity recorded. Timeout remains best effort in-process.
- DAX Studio/VS Code/Theme Forge integrations, report PBIR indexing/writes/themes, script documents/gallery, project synchronization/recovery, profiling/Pivot Lab and Optimize extensions are deferred. Do not report them as failed implemented features or as complete.

## Evidence and verdict

Keep source/HEAD/dirty-tree identity, per-stage exit codes, binary hashes, screenshots and scenario steps. Preserve failures before retrying. For defects: expected/actual, minimal synthetic reproduction, severity, affected slice and whether regression or pre-existing. Record tests as PASS / FAIL / BLOCKED_ENV / NOT_RUN, never infer live coverage from controlled readers.

Return **GREEN / YELLOW / RED** with scope and unresolved gates: GREEN only for the actually tested scope with no blocking defect; YELLOW for material unverified gates or unresolved intermittent failures; RED for reproducible correctness/data-loss/privacy/undo failure. This is a QA recommendation, not release authorization. Route a consolidated defect packet back to Pro; final release decision remains with the owner.

## Final delivered identity

Product source and tests: **603936b41649da088ae01cd6edff6efcc01319b9** on `codex/pro-ai-handover-2026-09-11`. A final documentation/evidence-only commit follows this product commit; test the branch tip and record its SHA. No product source changes follow the stated product commit in this delivery.

Final runner `.tools/verification-delivery` exited 0 with all seven stages PASS. Committed compact evidence: `handover/2026-09-11/evidence/pro-2026-09-14/verification-summary.json`, `core-smoke.log`, `host-smoke.log`, `artifact-hashes.json`; both OBS-001 failures are beside them. Full intermediate logs stay in ignored `.tools`. Binary hashes establish this run's identity; rebuilt hashes can differ because build timestamps/toolchain are not a reproducible-binary claim.
