# Architect review: TE2 Enhanced Opus pass (2026-10-13)

Branch `claude/pbibench-opus-2026-10-13` @ `9160fd7`, base `43409e0`. I read PLAN, S2-S7, AGENTS.md and MCP_COMPANION_V1, then sampled the 8 files named by the caller plus the code that calls them (ScriptCompiler, MetadataRecipe host, DaxWorkbench Recovery, ThemeApplyForm, DaxDocument). This was a static review. I did not run any build or smoke.

## Verdict: GO. Call it a "usable V1 core pending Light QA", with the qualifications below.
I found no RED in the code I sampled. Every write path goes through preview, a stale check and then either one undo batch or a journal. The script lane says "not sandboxed" everywhere. Rename refuses textual mentions that TE2 does not track. Theme apply refuses unknown schemas.

## Defects (severity for a Light QA pass)
| # | Sev | Where | Defect | Fix |
|---|---|---|---|---|
| 1 | YELLOW | `Documents/RecoveryStore.cs:119`; `Dax/PbiBenchDaxWorkbenchForm.Recovery.cs:115,142,205`; `Automation/PbiBenchScriptWorkspaceForm.cs:177,189` | **Two TE2 instances share recovery copies.** Instance B offers A's live drafts. Once restored, B writes under the same id, and `Save` deletes A's versions, so the last writer wins. When B closes a tab with "Don't save", A's copy is deleted. If A then crashes, its unsaved text is lost. | Store an owner (pid + start time) in the manifest and offer only copies whose owner is gone. A restored tab gets a new id, and the source copy is deleted only after the new copy is written. |
| 2 | YELLOW | `Project/ReviewedFilePlan.cs:207-227` | All backups are verified first, then the files are committed one after another, each with `Flush(true)`. Nothing re-checks a file right before its own commit. An external save during a long apply (up to 256 files) is overwritten silently. The journal holds the pre-review bytes, not that edit. | Re-hash each target just before `CommitContent`/`File.Delete` and treat a mismatch as a failure, which rolls back. |
| 3 | LOW | `ReviewedFilePlan.cs:239` + `Report/PbiBenchThemeApplyForm.cs:145-148` | `Mark(Applied)` runs outside the try. If it throws, the files are already written but the UI says "Apply refused", and "Restore this apply" stays disabled. The journal still shows as Applying. | Wrap it in `TryMark` and return the result anyway. |
| 4 | LOW | `PbiBenchThemeApplyForm.cs:163` | `RestoreLast` does not catch `InvalidDataException` (corrupt backup or journal; it is not an IOException), so the error reaches TE2's unhandled-exception dialog. The S5 form catches all exceptions. | Add the catch. |
| 5 | LOW | `Report/ThemeApplyPlan.cs:87-97,120` | The theme file name comes only from the theme `name`. If it matches an existing non-theme RegisteredResources item, that file is replaced and the package ends up with two items of the same name. The journal can restore it. | Refuse or suffix when the name is already taken by an item that is not a CustomTheme. |
| 6 | LOW | `Project/PbiBenchProjectWatcher.cs:65,85` | `Error` sends a rescan signal but never restarts the watcher (after the root is deleted or moved, the banner goes quiet). The directory-event filter is racy: the `ProjectWatcherSmoke` flake has been seen twice and is a risk for the Verify gate. | Recreate the watcher after a non-overflow error. Make the smoke assert on the manifest diff, not on whether a signal arrived. |

## Boundaries (question 2): no violation found
- Core has no WinForms, TOM, AMO, HTTP or Newtonsoft imports. S6 replaced the planned Newtonsoft pin with a bounded reader; that choice is documented and makes neutrality stronger.
- The Disk, Loaded, Live, Git and Baseline stamps are distinct types. Unknown PBIR schemas are read-only, enforced by `ThemeApplyPlan.cs:41`.
- No sandbox claim anywhere. Only the companion references `TE2Enhanced.Mcp`, and it is not in the sln. The doc's net8.0 + RollForward matches the csproj; only the PLAN still says "net10".
- Qualification: **TE2's own Save is not hooked** (S5). The AGENTS rule "no silent overwrite" holds for PbiBench writes. It does not hold for a TE2 model save over TMDL files edited outside TE2: there is only an after-the-fact "Review save target" check. That is upstream behaviour, not a regression, but the V1 claim must say so.

## V1 gaps (question 3)
1. A pre-save conflict prompt on TE2 Save, which needs a product decision on touching the upstream save path.
2. Recovery that is safe with several instances (#1).
3. No retention or delete UI for `%LOCALAPPDATA%\TE2Enhanced\journal`. Backups of report.json stay there indefinitely. The location is disclosed but never pruned.
4. Nothing verified natively or live yet: Desktop/XMLA, the positional arguments passed to DAX Studio, Power BI Desktop accepting a spliced report.json, an MCP client round trip.
5. PBIR-Legacy, bookmarks and report-level measures are not indexed (they show as Unresolved). Recipes cover measures only.
6. Integration branch only: per the slice notes, nothing is pushed or merged. CI on `9160fd7` was not checked in this review.
