# Light QA handoff: TE2 Enhanced (Opus pass, 2026-10-13)

Your role is independent Windows QA, not implementation. Read AGENTS.md, then [PRO_IMPLEMENTATION_STATUS.md](PRO_IMPLEMENTATION_STATUS.md) (2026-10-13 section), [../2026-10-13/PLAN.md](../2026-10-13/PLAN.md), the slice notes in [../2026-10-13/slices/](../2026-10-13/slices/) and [../2026-10-13/ARCHITECT_REVIEW.md](../2026-10-13/ARCHITECT_REVIEW.md).

Constraints:
- Do not silently fix production code.
- Do not merge, push or release.
- Everything below is implemented for review. None of it is independently accepted.

The 2026-09-14 handoff matrix (previous revision of this file, see `git log -p` on it) still applies to those slices and is included below as "Inherited".

## Identity
- **Branch:** `claude/pbibench-opus-2026-10-13` on `github.com/julian-passebecq/TabularEditor_J`.
- **CI mirror:** private GitLab project `juliandatapass-group/tabulareditor-j`.
- **HEAD:** test the branch tip. The exact SHA is in the "Final identity" section of PRO_IMPLEMENTATION_STATUS.md. Record `git rev-parse HEAD` before testing.
- **Base:** `43409e0`, the 2026-09-14 Pro delivery. It was never published before 2026-10-13 and is now included in this branch.
- **Remote CI on this branch:**
  - GitLab pipeline on the owner's local Windows runner: Verify 8 stages, plus a portable package with SHA256SUMS and a self-test of the packaged MCP companion.
  - GitHub `PbiBench TE2 integration` (windows-latest, full Verify).
  - GitHub `PbiBench foundation` (Core).
  - The URLs are listed in the status file. CI does not replace your native journey.

## Prerequisites (this machine as observed 2026-10-13)
- Windows 11, VS BuildTools 18 (vswhere picks `...\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\MSBuild.exe`).
- .NET Framework 4.8 reference assemblies.
- System SDK 9.0.101; private SDK 10.0.401 in the ignored `.tools/dotnet`; `.tools/nuget.exe`; Git on PATH.
- To run the MCP companion: any .NET 8+ runtime.

## Build and automated lanes
```powershell
git status --short --branch; git rev-parse HEAD
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Verify-PbiBench.ps1 -DotNetPath .tools/dotnet/dotnet.exe -NuGetPath .tools/nuget.exe -EvidenceDirectory $PWD/.tools/light-qa
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/ci/Package-PbiBench.ps1 -OutDirectory $PWD/.tools/light-qa-dist -DotNetPath .tools/dotnet/dotnet.exe
```

**Verify stages (8):** restore-legacy, restore-core, antlr-debug, host-release (actual Release host rebuild), core-smoke (net10), mcp-smoke, host-smoke-build, host-smoke-run. Exit 0 is required.

**Package:** builds `TE2Enhanced-<sha8>-portable.zip`, which contains the Release host plus `mcp/`, together with SHA256SUMS.txt and source-identity.json.
- It is unsigned and is not an installer. The legacy `.vdproj` installer cannot be built headless and was not touched.
- It installs nothing globally: extract the zip and run `TabularEditor.exe`.

**Artifacts:**
- Executable: `TabularEditor/bin/Release/TabularEditor.exe`.
- Harness: `PbiBench.Host.Smoke/bin/Release/net48/PbiBench.Host.Smoke.exe`.
- Synthetic BIM: `projectmanagement/evidence/S001/lead/PbiBench-Demo.bim`.
- Create PBIP, PBIR, Git and theme fixtures in an owned temp folder.
- Never put real rows, private DAX or credentials in evidence.

## Matrix for the new 2026-10-13 work (risk order)
| Area (IDs) | Execute and inspect |
|---|---|
| Recovery and multi-instance (DAX-013, AUTO-001) | Opt in. Edit in two TE2 instances at once and kill one. The survivor must not offer the other instance's live drafts. Restart after the crash: drafts are restored as unsaved tabs. "Don't save" deletes only your own copy. Check retention and the disclosed location `%LOCALAPPDATA%\TE2Enhanced\recovery`. |
| Reviewed file writes (PROJ-004, REPORT-002) | Apply a theme to a **copy** of a real Desktop PBIP. Edit report.json externally during review, then apply: it must be rejected with nothing written. Do a normal apply, reopen in Power BI Desktop (theme shown, no repair prompt), then Restore journal and reopen (old theme, byte-exact). Unknown or newer schema: read-only with a reason. Journal folder `%LOCALAPPDATA%\TE2Enhanced\journal`: delete or prune old journals. |
| TE2 save over externally edited TMDL (PROJ-003) | **Known limit:** TE2's own Save is not hooked. Verify that the Project banner and "Review save target…" list the files, and that the UI states the limit. Report this as a limit, not as a regression. |
| DAX authoring (DAX-010/011) | Workbench: Ctrl+Space inside `'Table'[`, bare `[`, strings and comments (no list there); Ctrl+Shift+Space; Ctrl+F7 / F8 advisory issues. Expression editor assistance (opt-in, Tools > PbiBench): insert, Ctrl+Z, Enter/Esc accept or cancel, then TOM undo. Look for false positives on calculation groups, UDFs and semicolon-locale models. Every check is advisory and lexical. |
| Multi-document Workbench (DAX-013) | Up to 32 tabs. Each file can be open in only one tab. Ctrl+Tab. The dirty prompt on close-all stops everything on Cancel. External modify or delete is detected on activation (Reload / Keep mine). Formatter reply held for tab A, then switch to an equal-text tab B: B must stay untouched. Save failure keeps text and dirty state. |
| DAX Studio (DAX-014) | Installed: it launches with server and database labels only and the query goes to the clipboard. Confirm DAX Studio actually connects from those positional arguments; this is unverified. Not installed: the query is saved as .dax and the reason is shown. Check that no connection string ever appears in UI, logs or arguments. |
| Usage and lineage (MODEL-002, REPORT-001) | Semantic View Usage / Unresolved tab on a real model, with navigation. Report index of a real PBIP: counts against Desktop, the Unresolved filter, Goto. "Used in report" column after indexing; it disappears after a rename. Time a large report (indexing is synchronous on the UI thread). |
| Recipes and scripts (AUTO-001/002) | Rename preview lists dependents. Apply, then one Undo restores everything. Rename is refused when references are unresolved. Format string and description validation. Script workspace: Problems list from a compile error; trust prompt for the exact text; any edit clears trust; failure rolls back model changes. Confirm the UI never claims a sandbox. |
| Project (PROJ-002/003/004) | Identity line, Recent list, distinct Disk/Loaded/Live/Git/Baseline stamps. External .tmdl edit or git checkout gives one banner within about 1 s; a large checkout overflows and still converges; deleting the root folder gives a clear state. Open in VS Code with user install, system install, a path with spaces, and not installed. |
| Explore / Optimize (EXP-002, OPT-001) | Profile and coverage drafts open unsaved and do not execute before F5; F5 after renaming the profiled column is refused. Curated BPA: rule errors = 0 on a real model, no rule file timestamps change, a same-ID user rule wins. Assertions: navigation, bidi allowlist prefilled from the annotation. |
| MCP companion (MCP-001) | Tools > PbiBench > Publish read-only snapshot: expressions are off by default. Register `mcp\TE2Enhanced.Mcp.exe` in one MCP client: list measures; get_measure shows `expressionWithheld` without the opt-in. With TE2 closed, the companion still answers from the snapshot. A future contract or credential-like text is refused. |
| Native UI (all new forms) | Keyboard-only Tab/Enter/Esc, focus return, minimum size and resize, real 100/150/200 % DPI, multi-monitor moves. Screenshots of normal, error, truncated and dirty states. Harness layout checks do not certify this journey. |

## Inherited matrix (2026-09-14 slices, unchanged)
Ordinary TE2 open/edit/undo/save/reopen, trusted C# and inherited BPA; Quick Open and Semantic View freshness; multiple results (0/1/2/32/33 sets, per-set export, cancel between sets); single-document formatter consent and S001 guards; project discovery bounds (UNC, junctions, access denied, worktree `.git` file); table preview drafts. See the previous revision for the step lists.

## Open gates and observations
- **OBS-001 is OPEN.** `File.Replace` occasionally fails with "Unable to remove the file to be replaced" on files written moments earlier. It has been seen on DAX document save, CSV export and now `ReviewedFilePlan` commit.
  - The theme smokes accept exactly one such failure with a verified byte-exact rollback, then repeat on a second fixture.
  - RecoveryStore avoids in-place replace.
  - Investigate locking (Defender, Search indexer) and capture the HRESULT. Do not add retries.
- **Watcher flake:** `ProjectWatcherSmoke` flaked twice (late directory events). The product filter and smoke hardening are in this branch. Repeat the host smoke at least 5 times and report the pass rate.
- **Live gates:** no live Desktop/XMLA/auth profile is certified (DAX-006 BLOCKED_ENV). Request approved disposable fixtures from the owner. Never attach to unknown local servers or use discovered credentials.
- **Legacy tests:** `TabularEditorTest` still lacks VisualStudio.QualityTools references. It stays isolated and is not counted as passing.
- **Not built (V2):** PBIR-Legacy and bookmark indexing, report-level measures, AUTO-003 gallery, OPT-002 VPAX/DMV, REPORT-003 reference rewrite, MCP write proposals. Do not report these as failed features.

## Evidence and verdict
- Record source/HEAD/dirty state, per-stage exit codes, package hashes, screenshots and scenario steps.
- Preserve failures before any retry.
- Classify each check as PASS, FAIL, BLOCKED_ENV or NOT_RUN.
- Return GREEN, YELLOW or RED for the tested scope:
  - **RED:** reproducible data loss, overwrite, privacy or undo failure.
  - **YELLOW:** unverified material gates, or OBS-001 recurring.
- Send one consolidated defect packet back to Pro. The release decision belongs to the owner.
