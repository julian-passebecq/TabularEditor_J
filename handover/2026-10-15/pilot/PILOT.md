# PbiBench Desktop pilot (2026-10-15)

Target bytes: the vault package recorded in `../PROVENANCE.md` (pbibench `2.28.9784.4682-e7a8b06`, galaxy-release-vault). It was downloaded again with authentication; an anonymous request returned 401. All five files match the receipt:

| File | SHA256 |
|---|---|
| `TE2Enhanced-e7a8b067-portable.zip` | `3458fac2521d1154a0a4d11b43c9d5248e2ecc48523ea7275739fca8a46799bd` |
| `SHA256SUMS.txt` | `7050a66765e69cfd4b48657c599f416abd22ca8ce2ce0f452a906c4cbef5a7d7` |
| `MANIFEST.txt` | `80f3d3c4de996348f0af0971d3d51a4c47cbb74fc25ed89019006534e5bc5e12` |
| `source-identity.json` | `52b17e2167d48b212cb42fe3387d119fd5dd1901f105c380efc959600324b61c` |
| `qualification.json` | `e8080604f7ff8709ea3a094dbecf6fff35f101a381df41bed69a0a96f815e13d` |

The extracted `TabularEditor.exe` (`7127912a...`), `PbiBench.Core.dll` (`c3598a7f...`) and `mcp/TE2Enhanced.Mcp.dll` (`164ac168...`) match `SHA256SUMS.txt`. The product was not rebuilt. The test harness (`PbiBench.Host.Smoke.exe`, built from `cd03a34`) ran from a copy of the extracted package. An in-process probe showed that it loaded the vault `TabularEditor.exe`, `PbiBench.Core.dll` and the TOMWrapper embedded in that exe (`evidence/harness/`).

Everything ran locally on one Windows 11 PC with Windows integrated auth and no credentials. Fixture: `../fixture/Gate.pbip` (synthetic calculated table `Sales`: North 10, South 20, East 5).

## Results

| # | Check | Result | Evidence |
|---|---|---|---|
| 1 | DAX oracle: the same 6 queries on local SSAS and on Power BI Desktop | PASS: all 6 match cell for cell, with no errors (totals, grouping, AVERAGE, DIVIDE, RANKX, BLANK, DIVIDE by 0, CONCATENATEX) | `evidence/oracle-compare.md`, `evidence/ssas/results-ssas.json`, `evidence/desktop/results-desktop.json` |
| 2 | Engine revision | PASS: SSAS 2022 16.0.42.229 (Tabular, compat 1600) and Desktop 2.158.1304.0 with AS 17.0.87.18 (compat 1606). The fixture's level 1600 runs on both. | `evidence/engine-revision.md` |
| 3 | Reopen/render and rollback in Desktop, driven by FlaUI | PASS: the render shows 35 and East 5 / North 10 / South 20. The vault TE2 wrote `TE2 Gate Double` (70). Close with "Don't save", then reopen: the measure is absent. Save in Desktop: the measure is in `Sales.tmdl`, and TE2 reloads it. File rollback, then reopen: absent, and the files hash-equal the baseline. | `evidence/desktop/render-rollback.log`, `render-*.png`, `saved-Sales.tmdl`, `harness-gate-*.log` |
| 4 | DAX Studio and TE2 launch against the model, with no global config change | PASS with a note: DAX Studio 3.0.11 opened with TE2's `/server= /database=` arguments, connected without a dialog, and returned 35. The vault TE2 connected and showed `Sales`. Config content is unchanged (`Preferences.json` hash equal; External Tools folder and `%APPDATA%` unchanged). | `evidence/desktop/tools-launch.log`, `config-diff-tools.json`, `daxstudio-*.png`, `te2-connected.png` |
| 5 | Known gap: cancel/timeout and a long server query | FAIL (gap confirmed): the client returns in 2.75 s (cancel) and 3.13 s (1 s timeout), and the executor can be used again afterwards. The engine command stays in `DISCOVER_COMMANDS` for 4.5 s and 3.1 s after the client returned, and its CPU time (5.8 s and 6.0 s) is about its whole runtime, so the query most likely ran to completion. Against Desktop, the client-side cancel returned in 726 ms and 732 ms. | `evidence/ssas/cancel.log`, `evidence/ssas/CancelProbe.cs` |
| 6 | Remote XMLA / Entra | PASS with one product FAIL (run later the same day, 22:07): the vault TE2 connects to a Power BI XMLA endpoint as a service principal, reads the model, writes it and rolls it back. The DAX oracle matches SSAS 6/6. `TryCreate(handler)` drops the password when it clones the connection, so the product executor cannot query that connection. | `../remote-xmla/REMOTE_XMLA.md` |

## Notes
- The SSAS check used the default instance (`localhost`, service `MSSQLServerOLAPService`, Tabular). The throwaway database `tmp_pbibench-pilot_20261015` was created, used and dropped. The instance's database list was empty before and after. No other database was opened.
- SSAS rejects `defaultPowerBIDataSourceVersion: powerBI_V3`, so the SSAS copy was deployed with `PowerBI_V1`. This has no effect on a calculated table.
- Check 5 used a scratch probe that calls the vault `PbiBenchAmoDaxQueryExecutor`. The stock `Run-LiveGate.ps1` accepts only `tmp_te2_yyyymmdd` database names (`evidence/ssas/live-gate-harness.raw.log`). The 10 s limit is met only because the test query is short. A longer formula-engine query keeps the server busy after the client has been released. This is an engine limit already recorded in `../DESKTOP_GATES.md`.
- The Desktop test copy's `report.json` got two visuals (card and table) so the render can be read through UIA. The semantic model files were byte-equal to the fixture. `render-baseline.png` and `render-after-file-rollback.png` are pixel-identical, as expected after a file rollback.
- New facts about Desktop 2.158: the "Don't save", PBIR upgrade and Auto recovery dialogs can be reached through UIA. A fresh open shows a "calculated tables need to be manually refreshed" banner, but the visuals still render.
- Side effect: at start-up, TE2 rewrites the shared extraction cache `%LOCALAPPDATA%\TabularEditor\TOMWrapper14.dll` with its embedded build. Any TE2 2.x does this. It is a cache, not configuration, but it matters when several TE2 versions are installed side by side. DAX Studio wrote its own log and a query-history file.
- Cleanup: every Power BI Desktop, msmdsrv, DAX Studio and TE2 process this run started was closed through its own window and confirmed exited. None was killed, and the Power BI Desktop instance of another session was not touched. The AS workspaces and model copy were removed. The AutoRecovery folder had the same 60 entries before and after.
- Evidence was scrubbed of user paths, emails and secrets (`evidence/scrub.log`).
