# DAX-006 live gate: local SSAS throwaway database (2026-10-13)

## Authorization
Julian authorized the gate on 2026-10-13 (relayed by the lead session "Opus project status review"). The scope was:
- one throwaway database `te2_tmp_<date>` on the local instance `MSOLAP$MSSQLSERVERTABU` (SSAS 2022, `MSAS16`);
- no other database on that instance is read or modified;
- the database is dropped at the end.

The gate checks only `Databases.ContainsName(<target>)` and refuses to run when that name already exists.

## How to run
```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Verify-PbiBench.ps1 -DotNetPath .tools/dotnet/dotnet.exe -NuGetPath .tools/nuget.exe -EvidenceDirectory $PWD/.tools/verify
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/live/Run-LiveGate.ps1   # -Server, -Database te2_tmp_yyyymmdd
```
- Authentication is Windows integrated. No credentials are read or stored.
- The gate never runs in CI: without `PBIBENCH_LIVE_SERVER`/`PBIBENCH_LIVE_DATABASE` the host smoke prints `NOT_RUN`.

## What it proves (`PbiBench.Host.Smoke/LiveGateSmoke.cs`)
1. **Fixture.** The gate builds a synthetic calculated table (`DATATABLE`, 3 rows) and a `Total Amount` measure, refreshes them, and drops the database in `finally`. The drop is verified.
2. **TE2 loads the live model.** It uses the real `TabularModelHandler(server, database)`. `PbiBenchAmoDaxQueryExecutor.TryCreate` clones the live connection, and the label shows server / database only.
3. **Multi-rowset query.** The query returns 2 result sets: 3 rows ordered, plus the measure total of 35 computed by the engine.
4. **Row limit.** `MaxRows=2` truncates the result with reason `RowLimit`.
5. **Query error.** `EVALUATE NoSuchTable` fails with reason `Query`, and the provider text is not echoed.
6. **Cancel and timeout.** Both report `Cancelled`, with `UserCancellation` and `Timeout` respectively, and never return results. The executor stays usable afterwards.

Last run: PASS. Cancel returned after 11.3 s and timeout after 10.7 s, both on a query that takes about 11 s.

## Product defect found and fixed
`AmoDaxQuerySession.Execute` ignored errors in `XmlaResultCollection`. On SSAS 2022 a DAX error comes back as an XMLA message with no reader, so the Workbench showed a failed query as **Completed with no rows**. Execute now throws when any `XmlaError` is present, and the executor maps it to `Failed / Query` without provider text.

## Known limit confirmed live (client side fixed 2026-10-15)
Update 2026-10-15: the executor now ends the query session after 2 s, so cancel and timeout return in about 3 s. The engine-side limit below still holds. See `handover/2026-10-15/DESKTOP_GATES.md`.

Cancel and timeout do not interrupt a query that is bound by the formula engine.
- Two methods were tried:
  - `Server.CancelCommand()`, the product code;
  - an XMLA `<Cancel><SessionID>` sent from a second connection. The probe is in `.tools/live-diag`, local and ignored.
- With both methods the engine ran the query to completion: 130 to 350 s on larger probes. The executor then reported `Cancelled`, without data.
- The UI stays in "running" until the engine finishes, so cancellation is best effort, as documented.
- The same fix failed twice, so the work was stopped. Next candidates:
  - cancel by connection SPID found through `DISCOVER_COMMANDS`;
  - closing the query connection;
  - an architect review.

## Not covered
- Power BI Desktop: it is not installed.
- Remote XMLA, Entra authentication, and DAX Studio connecting from positional arguments.
