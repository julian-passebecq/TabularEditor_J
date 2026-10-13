# TE2 Enhanced - Independent Light QA Report

**Commit** 823003f7e25258e15bcd49aa5cd6e0d2d598cb4f (detached, clean before and after; differs from product commit c23e411 only by `handover/2026-10-13/RESUME.md`)
**Date** 2026-10-13 | **Worktree** `.claude/worktrees/wf_b69004a2-a5b-1` | **Evidence root** `.tools/light-qa/`
**Toolchain** msbuild 17.12.12.57101 (VS2022 Community via vswhere -latest, not "BuildTools 18" named in the handoff); dotnet private SDK 10.0.401 (used), system 9.0.100-preview.2 / 9.0.101; FlaUI.UIA3 4.0.0 (net48 harness). Display state: 150% on two monitors, 175% on a third (not changed).

## Verdict: YELLOW

**Tested scope**: Verify-PbiBench 8/8 stages; host smoke x10; Core smoke x5; OBS-001 probe (500 iterations, C: and D:); package build, hashes and zip contents; MCP companion adversarial stdio, snapshot and credential cases; Core-level adversarial tests of ReviewedFilePlan, RecoveryStore, DAX Studio handoff, PBIR index, theme apply and DAX diagnostics; privacy diff scan 43409e0..823003f; native UI journey with FlaUI on synthetic data (ordinary edit/undo/save, Tools > PbiBench dialogs, DAX Workbench, recipes, script workspace, report index, MCP publish).

**Why not GREEN**: OBS-001 still recurs (6/500 on C: TEMP; 1/5 Core smoke runs) and is open; material gates are unverified or blocked (live endpoints, DPI 100/200%, multi-monitor, DAX Studio connect, Power BI Desktop reopen, MCP client round trip); the MCP companion crashes on type-mismatched requests; the Project dialog clips controls at minimum size.
**Why not RED**: no data loss, overwrite, undo or privacy failure was reproduced. ReviewedFilePlan rollback was byte-exact, external edits were preserved, restore was byte-exact, and no network or credential leak was found in the diff. The credential-guard gap (D3) is the closest to RED; the owner should rule on it.

## Results by matrix area

| Area | PASS | FAIL | BLOCKED_ENV | NOT_RUN | Key evidence |
|---|---|---|---|---|---|
| Setup, verify, smoke stability | 5 | 0 | 0 | 0 | `verify/summary.json` (8/8); `stability/summary.csv` (host 10/10); `stability/core-smoke-02.log` (OBS-001 tolerated) |
| OBS-001 probe | 1 | 0 | 0 | 0 | `probe-temp.txt`, `probe-temp-2.txt`, `probe-worktree.txt` (reproduced, still OPEN) |
| Packaging (build, hashes, MCP self-test, zip) | 4 | 0 | 0 | 0 | `dist/SHA256SUMS.txt`, `mcp-package-selftest.txt`; first build failure kept in `package-first-failure.err` |
| MCP companion robustness and credential guard | 0 | 3 | 0 | 0 | `mcp/mcp-protocol-cases.txt`, `mcp-snapshot-cases.txt`, `mcp-creds.txt` |
| Core adversarial (file plan, recovery, handoff, PBIR, theme, DAX) | 7 | 5 | 0 | 0 | `adv-out.txt`, `repro-out.txt` |
| Privacy / diff scan | 1 | 0 | 0 | 0 | `diff.txt`, `added.txt`, `diff-docs.txt` |
| Native UI: core model edit/undo/save | 5 | 0 | 0 | 0 | `uia/shots/a-ord-*.png` |
| Native UI: Tools > PbiBench dialogs, keyboard, layout | 8 | 4 | 0 | 1 | `dialogs-run9.log`, `dialogs-run13.log`, `shots/d4-Project-min.png` |
| Native UI: workbench, recipes, scripts, report index, MCP, handoff | 12 | 2 | 0 | 0 | `a-dax-*`, `a-rec-*`, `a-scr-*`, `a-rep-*`, `mcp-uia/mcp-run.txt` |
| DPI, multi-monitor, live endpoints | 0 | 0 | 3 | 0 | `dpi-record.txt` |
| Native journey, multi-instance kill, TE2 own Save over external TMDL | 0 | 0 | 0 | 3 | none |
| **Total** (excluding the scope-verdict meta check) | **42** | **14** | **3** | **4** | |

## Consolidated defect packet

None is flagged as a regression. Slice/feature IDs below are feature areas, since no slice IDs were supplied with the inputs. Severity key: Y = YELLOW (material), L = LOW.

| # | Sev | Feature | Minimal synthetic repro | Expected / actual |
|---|---|---|---|---|
| D1 | Y | MCP companion JSON-RPC | `TE2Enhanced.Mcp.exe --snapshot <valid synthetic>`; send `{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"list_measures","arguments":{"limit":"5"}}}`. Same with limit 1.5, includeHidden "no", name 5, method 5 or {}, protocolVersion 5. | Expected: JSON-RPC error and the next ping answered. Actual: unhandled InvalidOperationException/FormatException in `McpServer.Handle`, exit -532462766, no reply. A string `limit` from an LLM client is realistic. |
| D2 | Y | Project dialog layout | Tools > PbiBench > Project, resize to minimum (975x600 px at 150%). | Expected: all toolbar controls reachable. Actual: "Check save target...", "Restore journal...", "Export context..." fall outside the window and "Review change..." is cut. Still outside at 1400 px wide. |
| D3 | L (owner may raise) | MCP credential guard | Snapshot with `ModelRevision='client_secret=zzz'`, or `Relationships[0].FromTable='a;Password=hunter2'`, or a measure description of `Password = x`, `password: x`, `Authorization: Bearer ...`, `...?sig=abc`, `Data Source=...;Initial Catalog=...`; call `get_model_summary` or `list_relationships`. | Expected: refused, per docs ("never contained", "refuses credential-looking text"). Actual: only ModelName and table/column/measure fields are scanned, for 5 exact markers. The variants are served; ModelRevision is echoed. |
| D4 | Y | OBS-001 (File.Replace) | `Scripts/diagnostics/Probe-FileReplace.ps1 -N 500` on C: %TEMP%; Core smoke run 2 of 5. | Expected: no refused replace, or a decided policy. Actual: "Unable to remove the file to be replaced" 6/500 on C: and 0/500 on D:; the product fails visibly with rollback and no data loss. Owner decision on bounded retry for error 1175 is open. |
| D5 | L | MCP snapshot loading | `snapshot-v1.json` of 100 NUL bytes, or `{"ContractVersion":1,"Tables":[null]}`, or a null ModelName; call `get_model_summary`. | Expected: tool error and the server stays alive. Actual: XmlException or NullReferenceException (`ModelSnapshotSerializer.Validate`) kills the process. Normally published snapshots are not affected. |
| D6 | L | PBIR index | Overwrite any of visual.json, page.json, pages.json, report.json, version.json or definition.pbir with bytes `7B 22 FF FE 22 7D`; `PbirReportIndex.Load`. | Expected: a diagnostic, other pages still indexed. Actual: DecoderFallbackException (6/6 file kinds); the host shows "Report could not be indexed". |
| D7 | L | PBIR index | `definition/pages/jp` as a junction (`mklink /J`) to an outside folder with a valid page.json, listed in pageOrder. | Expected: reparse point skipped, as `ExternalChangeDetector` does. Actual: outside content is read and indexed (read-only). |
| D8 | L | ReviewedFilePlan.ResolveTarget | `Add(new ReviewedFileChange("CON.json" or "NUL.txt" or "aux/x.json" or "x?.json" or "x*.json", Create, ...))`; theme named "CON". | Expected: rejected, per the summary. Actual: all accepted. A Move to CON.json succeeded as a normal file on Win11, so no observed harm. |
| D9 | L | Theme apply | report.json customTheme path `../../definition/report.json`, `C:\Windows\win.ini`, `x:y` or `NUL`; `ThemeApplyPlan.Create`. | Expected: `ThemeApplyRefusedException`. Actual: raw ArgumentException; no write or delete; the host prints a generic message. |
| D10 | L | DAX Studio handoff | `DaxStudioHandoff.CheckLabel("-config" or "/file" or "@response.txt" or a label with U+2028 or U+202E, "Server")`. | Expected: refused (option-like or bidi characters). Actual: accepted as positional argv; no shell injection (UseShellExecute=false). DAX Studio behaviour is unverified. |
| D11 | L | DAX diagnostics | `EVALUATE VAR a = b VAR b = 1 RETURN ROW("x", a)` | Expected: DAX004 on `b` (S2 scope rule). Actual: no finding (advisory false negative). |
| D12 | L | Packaging | (1) Run `Package-PbiBench.ps1` from powershell.exe launched by a PowerShell 7 parent. (2) Search the shipped DLLs for `D:\PROJ`. (3) `.gitlab-ci.yml` lines 37 and 45. | (1) "Get-FileHash is not recognized" because the PSModulePath leaks; exit 1 with the zip but no SHA256SUMS; the retry passed. (2) Build-path PDB reference remains in PbiBench.Core.dll and TE2Enhanced.Mcp.dll (no user name). (3) Unpinned nuget.exe and dotnet-install.ps1. |
| D13 | L | Quick Open ranking | Ctrl+P, type "Total Sales". | Expected: exact name first. Actual: "Total Cost" is listed first and Enter opens it. |
| D14 | L | Keyboard / Esc | Open Project, Recipes, Table preview or Profile draft and press Esc; in DAX Workbench or the script workspace press Tab. | Expected: Esc closes; a keyboard route out of the editor exists. Actual: Esc does nothing in those 4 dialogs; Tab never leaves the code editors. |
| D15 | L | DAX Workbench offline | Open the Workbench on a file-loaded model, change the status text, press F5. | Expected: message that execution needs a live connection. Actual: silent return (`PbiBenchDaxWorkbenchForm.cs:273`). |
| D16 | L | MCP publish dialog | Tools > PbiBench > Publish read-only snapshot for MCP. | Expected: selectable destination, or a documented QA limit. Actual: always writes `%LOCALAPPDATA%\TE2Enhanced\mcp\snapshot-v1.json`, so a QA run overwrites a real user's snapshot. |
| D17 | L | Starter text / focus | Open DAX Workbench or the script workspace and close at once; open and close any dialog with Esc. | Actual: a "Save changes?" / "Close workspace anyway?" prompt for untouched starter text; focus returns to the menu strip, not the tree. |

## Open gates

1. OBS-001: owner decision (keep failing visibly vs bounded retry); no process identified (needs Process Monitor).
2. Live Desktop/XMLA/auth endpoint: BLOCKED_ENV (no approved disposable fixture).
3. DAX Studio positional-argument connect: not verifiable (not installed).
4. MCP client round trip (a real LLM client): not run; only stdio and self-test were covered.
5. Power BI Desktop reopen of a theme-applied report: not possible.
6. DPI 100% and 200% and multi-monitor moves: BLOCKED_ENV (a system setting; the machine is at 150/175%).
7. Full native journey (keyboard-only, screenshots of dirty/error/truncated states, all new forms) and two real TE2 instances with kill/restart: NOT_RUN. Core-level recovery and the host smokes pass.
8. TE2's own Save over an external TMDL: NOT_RUN (known limit; the UI statement of it is unverified). Also not run: the "Open model folder in VS Code" menu item (launches an external app); the expression-assist insert/accept/undo behaviour; the Relationships tab with a model that has relationships.

## What the owner must supply or decide

- A disposable Power BI Desktop / XMLA fixture with approved credentials, or an explicit waiver of the live gate.
- A DAX Studio install and an MCP client for the handoff and round-trip checks.
- A QA session at 100% and 200% DPI on a machine where the setting may be changed (or a waiver), plus a multi-monitor move test.
- A ruling on OBS-001 policy, and on D3 severity (privacy claim vs implementation).
- Triage of D1 and D2 (D1 is a small fix in the `McpServer.Handle` error path) before any release claim.
- Hashes in the handoff differ from the QA build (other commit, non-reproducible build, as documented); QA used `dist/SHA256SUMS.txt` from its own build.

*Evidence (logs, scripts, screenshots) stays local and unpublished under `.claude/worktrees/wf_b69004a2-a5b-1/.tools/light-qa/`. This copy was committed by the lead session.*
