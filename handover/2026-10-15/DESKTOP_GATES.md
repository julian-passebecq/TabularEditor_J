# Desktop gates: Power BI Desktop, DAX Studio, local XMLA, cancel (2026-10-15)

Base: `master` d646d37 (PR #4). Branch `claude/te2-desktop-gates-2026-10-15`. All runs on one Windows 11 PC, Windows integrated auth, no cloud, no credentials. Not merged: Julian asked for no merge in this repo.

## Environment
- Power BI Desktop Store 2.158.1304.0 (local AS 17.0.87.18).
- DAX Studio 3.0.11.982.
- SSAS 2022 tabular instance `localhost\MSSQLSERVERTABU` (16.0.42.229): throwaway database `tmp_te2_20261015` only, created and dropped by each run (verified `stillExists=False`). No other database was opened.
- The PC was under load from other work (CPU 70-86 %), so timings vary.

## Results

| Gate | Result | Evidence |
|---|---|---|
| Build and smokes (`Verify-PbiBench.ps1`, 8 stages) | PASS | `evidence/verify-summary.json`, `evidence/core-smoke.log`, `evidence/host-smoke-run.log` |
| Cancel/timeout on a long server query (SSAS, XMLA endpoint local) | PASS after fix: cancel 2.9 s, timeout 3.1 s (before: 11.3 s and 10.6 s) | `evidence/live-gate-ssas.log`, `evidence/cancel-probe/` |
| Power BI Desktop: open PBIP, TE2 connects to its AS port, writes a measure, query, cancel | PASS: Total=35, Double=70, cancel 0.8 s | `evidence/desktop-gate-live.log`, `evidence/pbi-desktop-te2-measure.png` |
| Power BI Desktop round trip to disk: Desktop saves the PBIP, TE2 reloads the TMDL | PASS: `TE2 Gate Double` is in the saved `Sales.tmdl` | `evidence/desktop-gate-disk.log`, `evidence/desktop-saved-Sales.tmdl` |
| DAX Studio handoff against Power BI Desktop | FAIL before fix (Connect dialog), PASS after fix: connected, query 70 / 35 | `evidence/daxstudio-desktop-connected.png`, `evidence/daxstudio-desktop-query.png` |
| DAX Studio against the tmp database on local SSAS | PASS: connected to `tmp_te2_20261015`, query 35 | `evidence/daxstudio-ssas-tmp-query.png` |
| DPI 150/200 on a real monitor, multi-monitor | NOT_RUN (manual; display settings are never changed by an agent) | listed in Julian's manual checks |
| Remote XMLA (Power BI service / Fabric), Entra sign-in | NOT_RUN (cloud tenant and login needed) | - |

## Defect 1 (fixed): cancel and timeout waited for the engine
Probe (`evidence/cancel-probe/`): one formula-engine-bound query (CROSSJOIN of two 6000-row series, FILTER, COUNTROWS), cancel sent 1.5 s after start, each method several times.

| Method | Client returned after | Engine busy until |
|---|---|---|
| No cancel | 13.5-49.8 s (load) | same |
| `Server.CancelCommand()` (old product code) | 18.2-56.2 s | same |
| XMLA `Cancel` by SPID | 16.5 s | 20.4 s |
| XMLA `Cancel` by SessionID | 19.9-26.8 s | 18.5-30.8 s |
| XMLA `Cancel` by SPID or SessionID with `CancelAssociated=true` | 1.5-1.7 s (20-200 ms after the cancel) | 4.3-45 s |

No method makes the engine stop the formula-engine step it is in: that is an engine limit. Ending the session does release the client at once.

Fix (`PbiBenchAmoDaxQueryExecutor`): a cancel or timeout first sends the command cancel, as before. If the command is still running after 2 s, the executor ends the dedicated query session once (`IDaxQuerySession.Abort`): an XMLA `Cancel` with the query session's `SessionID` and `CancelAssociated=true`, sent from a short-lived second connection. The blocked call returns and the run ends as Cancelled (UserCancellation or Timeout), with no data. The session id is validated as a GUID before it reaches the XMLA text. The TE2 editor connection is a different session: the live gate checks that it still works after the abort.
- New controlled smokes: abort exactly once for user cancel and for timeout, and no abort when the engine honours the cancel.
- Live gate now asserts cancel and timeout return within 10 s. Last run: 2918 ms and 3103 ms.
- Remaining limit (documented, not fixable from the client): the engine may keep computing the current formula-engine step in the background for some seconds. Nothing from it is shown.

## Defect 2 (fixed): DAX Studio ignored TE2's handoff arguments
TE2 started `DaxStudio.exe <server> <database>` (two bare positional arguments). DAX Studio 3.0.11 ignores them and shows its Connect dialog. Its own Power BI Desktop External Tools registration (`daxstudio.pbitool.json`) uses `/server="%server%" /database="%database%"`. `DaxStudioHandoff.BuildArguments` now produces `/server=<label>` and `/database=<label>`. The label rules are unchanged: no connection strings, no `=`/`;`, no option-like or control characters. Live: DAX Studio connected to Power BI Desktop (`localhost:22042`, model GUID) and to the SSAS tmp database with those exact arguments, with no dialog. A label that contains spaces is quoted as one argument (`"/database=Sales Model"`). This was checked only at command-line level, not live.

## Fixture
`fixture/Gate.pbip` is synthetic: a calculated `DATATABLE` table `Sales` (3 rows) and the measure `Total Amount`. It has no data source and no credentials. Desktop needs a manual refresh of calculated tables on first open: the live gate recalculates the model. Always open a copy: Desktop rewrites the folder when it saves. On save, Desktop 2.158 asks whether to upgrade the legacy `report.json` to PBIR. The run answered "Don't upgrade".

## How to rerun
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Verify-PbiBench.ps1 -DotNetPath <dotnet> -NuGetPath <nuget> -EvidenceDirectory $PWD/.tools/verify
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/live/Run-LiveGate.ps1 -Database tmp_te2_yyyymmdd
# Open a copy of handover/2026-10-15/fixture/Gate.pbip in Power BI Desktop, read the port from
# %USERPROFILE%\Microsoft\Power BI Desktop Store App\AnalysisServicesWorkspaces\<newest>\Data\msmdsrv.port.txt
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/live/Run-DesktopGate.ps1 -Port <port>
# Save the project in Desktop, then:
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/live/Run-DesktopGate.ps1 -Port <port> -Phase disk -TmdlFolder <copy>\Gate.SemanticModel\definition
```

## UI automation used
- APIs first: TOM/AMO for every model and query step, and the TE2 host smoke for all assertions.
- Windows UI Automation from PowerShell (`evidence/automation/`): the Desktop Data pane tree (the measure is visible), Desktop's Save button and teaching-popup Close, and DAX Studio's own dialogs (recovery "Cancel", close "Don't Save").
- Keystrokes and clicks were sent only after checking that the target window handle was in the foreground:
  - typing the DAX Studio query and pressing F5;
  - Desktop's WebView dialogs ("Don't upgrade", Auto recovery), which expose no UIA tree.
- On Desktop's close prompt, the run chose "Yes, I want to view the files later", so the auto-recovery files that existed before this run were kept.
- The computer-use MCP was not used: it would have waited for an approval.

## Cleanup
- Power BI Desktop, its AS instance and both DAX Studio windows that were started were closed.
- `tmp_te2_20261015` was dropped after each run.
- The DAX Studio autosave folder is empty.
- Another session's Power BI Desktop ("Energy") was running at the same time. It was not touched: every action targeted this run's process id and window handles.
