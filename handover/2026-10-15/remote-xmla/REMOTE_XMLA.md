# PbiBench remote XMLA check (2026-10-15)

Closes the last `BLOCKED_OWNER` row of the Desktop pilot (`../pilot/PILOT.md`, check 6).

Verdict: **PASS with one product FAIL.** The vault TE2 connects to a Power BI XMLA endpoint as a service principal, reads the model (identical to the fixture), writes and rolls back a measure, and the DAX oracle matches local SSAS 6/6. The product's DAX executor, created the normal way from that connection (`TryCreate(handler)`), cannot run any query on it: it clones `Server.ConnectionString`, and AMO returns that string without the password.

## Target and setup

| Item | Value |
|---|---|
| Product bytes | vault package `pbibench 2.28.9784.4682-e7a8b06` (galaxy-release-vault), downloaded again with authentication (anonymous: 401). Zip `3458fac2...799bd`. Extracted `TabularEditor.exe` `7127912a...`, `PbiBench.Core.dll` `c3598a7f...`, embedded TOMWrapper `2e980392...`: all equal to `../PROVENANCE.md` and `../pilot/evidence/harness/harness.log`. Not rebuilt. |
| Tenant | Fabric TEST tenant of datapass-fabric-bi PR #26 (roles in its `docs/pilot/IDENTITIES.md`); trial capacity, SKU FTL4 |
| Workspace | `dpf-test-pbibench-20261015`, new, created by the admin CLI session through the Fabric REST API, on the trial capacity. Roles: admin (User, Admin) and `sp-writer` (ServicePrincipal, Contributor). Left in place. |
| Identity | `sp-writer` (client id + secret read by name from Credential Manager `dpf-test-sp-writer`, passed on STDIN only, never printed). XMLA refuses the CLI user token. |
| Model | `Gate`: `../fixture/Gate.SemanticModel` deployed as is (compat 1600, `powerBI_V3` kept: no SSAS-style downgrade needed) via TOM from the vault package over XMLA, then refreshed. |
| Engine | Power BI XMLA endpoint, server version 17.0.94.13, Enterprise64, Tabular. Local SSAS side: 16.0.42.229 (`../pilot/evidence/engine-revision.md`). |
| Harness | `evidence/tool/Program.cs`, net48, assembly name `PbiBench.Host.Smoke` (the friend assembly the product already declares, as in the pilot's `CancelProbe`). It references the vault binaries without copying them; an assembly resolver loads them from the extracted package. |

## Results (final run 22:07)

| # | Check | Result | Detail |
|---|---|---|---|
| 1 | Deploy | PASS | `Gate` created and refreshed over XMLA as `sp-writer` (first run). The final run reused it. |
| 2 | TE2 connects and reads the model | PASS | `TabularModelHandler(<powerbi://...>, "Gate")` connected in 1.4 s, compat 1600. |
| 3 | Remote model = fixture | PASS | The table/column/measure/expression signature read over XMLA equals the one the same vault TE2 reads from the TMDL folder on disk. |
| 4 | Product executor on the XMLA connection | **FAIL** | `PbiBenchAmoDaxQueryExecutor.TryCreate(handler)` returns an executor, but every query fails in 1 ms with `Connection`. Cause: `handler.Database.Server.ConnectionString` holds `data source, user id, provider, application name, initial catalog`, without `Password`. A fresh AMO connect with it throws `InvalidOperationException: When interactive authentication is not supported, an external access-token is required`. |
| 5 | DAX oracle: XMLA vs local SSAS | PASS 6/6 | Same executor class and code path, built with the credentialed connection string (private constructor, by reflection). The cells equal the SSAS results of the pilot (`../pilot/evidence/ssas/results-ssas.json`): numbers exact, decimals within 1e-9, same column names, same query text. A raw ADOMD reference run (DAX Studio's ADOMD 19.x) also matches 6/6. |
| 6 | 1 s timeout on the remote endpoint | PASS (client side) | The client returned in 1.5 s, `Cancelled/Timeout`, and the executor ran the next query. Server-side duration was not observable here. |
| 7 | Write through TE2, then rollback | PASS | `AddMeasure("TE2 Remote Double")` then `SaveDB()`: the query returns 70. Delete, `SaveDB()`: a fresh TOM connection sees only `Total Amount`. |

Evidence: `evidence/remote-xmla.log`, `checks.json`, `results-remote.json`, `results-remote-adomd.json`, `oracle-compare-remote.md`, `oracle-compare-adomd.md`, `workspace.log`.

## Finding: propagation lag on a new workspace

For about 20 minutes after the workspace was created and `sp-writer` was added, 15-25 % of new SP connections did not see the database. The catalog list came back empty, or the server returned "Either the database 'Gate' does not exist, or you do not have permissions". The raw ADOMD path and the product path were hit in the same way, and so was TE2's `SaveDB` conflict check (`CheckConflicts` reconnects without a catalog, then `Databases[id]` throws). The P23 workspace, where the SP role is hours old, was 15/15 at the same time (list-only probe). At 22:06 the new workspace was also 15/15 (`evidence/tool/stability.ps1`). The earlier runs that failed this way are not counted. One of them left the test measure behind, and the final run removed it before its checks.

## Follow-up for the product (not done here: a product change needs a new build, re-qualification and a vault version)

- The executor must keep the credential when it clones a live connection: use the connection string the handler was opened with (TOMWrapper keeps it as `serverName`), or the access token, instead of `Server.ConnectionString`. Add a test for a password-bearing connection. Interactive Entra sign-in in the UI (the usual path for `powerbi://`) probably hits the same clone problem. It was not tested here because it needs Julian's login.
- `CheckConflicts` could retry or fall back when a newly created Power BI workspace still has propagation lag.

## Notes

- Nothing was written outside `dpf-test-pbibench-20261015`. The Fabric Studio workspaces were not touched. The P23 workspace was only read (connection and catalog list, `EVALUATE ROW("x", 1)`).
- The scripts in `evidence/tool` use `<repo>` and `<datapass-fabric-bi>` in place of local paths. No tenant id, client id, secret, UPN or item id is in this folder (checked by string search against the real values).

## Follow-up fix (2026-10-15, branch claude/pbibench-xmla-credential-clone)

- `TabularModelHandler.OpenedConnectionString` (internal) keeps the string the editor connected with. `PbiBenchAmoDaxQueryExecutor.TryCreate` now builds the query clone from `Server.ConnectionString` plus the password taken back from that string (`BuildQueryConnectionString`); labels keep server and catalog only. Covered by `PbiBench.Host.Smoke/CredentialCloneSmoke.cs` (synthetic strings).
- `CheckConflicts` connects with the editor's credentials, looks the database up by ID with `Find`, retries a missing database 3 times (1 s, 2 s), then throws a clear `InvalidOperationException` instead of a null reference, and disposes its connection.
- Not yet re-qualified: the vault build is still e7a8b06. Row 4 turns PASS only after a new build re-runs `evidence/tool` against the workspace.
- Re-qualified on 2026-10-15: vault build `pbibench 2.28.9784.42286-95fea60` runs row 4 PASS, and the oracle passes 6/6 through `TryCreate(handler)` (`../vault-95fea60/VAULT_95fea60.md`).
