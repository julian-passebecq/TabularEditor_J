# QA Finition report - source be3a6a06237f717a2c115ffb93c353e27a139c24 (detached worktree)
Zip: TE2Enhanced-be3a6a06-portable.zip  SHA256 1bbb819590a17574c238eb74418e1c40a29cd4b8384ab4bf8d2a300029eef7dd
Verdict: YELLOW (no data-loss/privacy/undo failure; unverified gates remain: manual DPI/multi-monitor, Power BI Desktop, remote XMLA/Entra, DAX Studio, FE-bound cancel)

| # | Check | Result |
|---|---|---|
| 0 | Verify-PbiBench.ps1, 8 stages | PASS (exit 0) |
| 0 | Package-PbiBench.ps1 | PASS |
| 1 | Zip hash = SHA256SUMS; 161 MANIFEST lines match; all required files present; no .pdb; no D:\PROJ / user path in Core/Mcp dlls; sourceCommit matches, dirty=false | PASS |
| 2 | Extracted TabularEditor.exe window in 3 s, closed gracefully | PASS |
| 3 | Mcp --self-test exit 0; real stdio client: initialize, initialized, tools/list (6 tools, readOnlyHint), 6 tools/call, write tool -32602; expression withheld without opt-in, present with opt-in; expression without flag refused; credential string refused (tool error) | PASS |
| 4 | Host smoke x5 | PASS 5/5, 0 OBS-001 |
| 5 | Live gate te2_tmp_20261013 | PASS, created and dropped (cancel 11262 ms, timeout 10614 ms, engine not interrupted: known limit) |
| 6 | Exit journeys | PASS via host smoke logs (see below); no UI-driven human run |
| 7 | Guide accuracy | PASS with minor wording defects |
| - | DPI 150/200 real monitor, multi-monitor | NOT_RUN (manual); only DPI proxy smoke PASS |
| - | Power BI Desktop, remote XMLA/Entra, DAX Studio | BLOCKED_ENV |

Journeys (smoke-log evidence): model->save/reopen->external change->explicit resolution: "S3 external change", "ProjectWindowSmoke", "Unresolved references/Usage", T05-T07 diagnostics; PBIR->lineage->theme on copy->journal restore: "ReportFormSmoke", "ThemeApplySmoke ... exact restore"; approved script->one undo: "ScriptWorkspaceSmoke ... per-version trust, one undo batch"; MCP: "MCP snapshot publisher" + my real-client round trip against a hand-made snapshot (host publisher not UI-driven by me). Live DAX diagnostics against Desktop not covered.

Defects (all minor, doc wording):
1. GETTING_STARTED step 2: "F8 runs advisory checks" - menu item is "Next advisory issue" (checks if stale, then jumps to next issue). "Ctrl+Shift+Space gives signatures" - menu label is "Parameter help".
2. Guide names "Review save target..." and "Project"/"Report"/"Publish read-only snapshot"; actual items are "Review save target for external changes...", "Project..." (and a separate "Project (read-only)..."), "Report (PBIR) index and lineage...", "Publish read-only snapshot for MCP...". Repro: open Tools > PbiBench.
Verified true: Tools > PbiBench menu, Ctrl+Space, F5, TE2ENHANCED_REPLACE_RETRY, data folders recovery/journal/mcp, --self-test.
Notes: MCP companion snapshot path via --snapshot used (no %LOCALAPPDATA% file touched; none existed). Processes started were closed. Worktree left in place.

## Addendum (implementation, after QA)
- Both guide defects are fixed in the next commit: shortcut wording and exact menu names. Only `GETTING_STARTED.md` changed, so the binaries in the zip are unchanged and only the guide file differs.
- The private artifact built from the final source is the GitLab job artifact `dist/` on branch `claude/pbibench-finition-2026-10-13`. Its SHA256 is recorded in `RESUME.md`.
- NO_MERGE (Galaxy Finition pass): the branch and PR are open for review and are not merged.
