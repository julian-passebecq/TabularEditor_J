# PbiBench vault build 95fea60 (2026-10-15)

`master` = `95fea60` (PR #9: the DAX executor keeps the service-principal password when it clones the live connection; `CheckConflicts` uses the editor's credentials and retries a missing database). That build replaces `e7a8b06` as the current vault build. `e7a8b06` stays in the vault unchanged.

Verdict: **YELLOW, no RED.** The remote XMLA executor row, the one FAIL in `../remote-xmla/REMOTE_XMLA.md` (row 4), is now PASS on the published bytes.

## Build and gates

| Gate | Result | Evidence |
|---|---|---|
| CI: `Verify-PbiBench.ps1` 8 stages (host-smoke-run includes `CredentialCloneSmoke`), package, MCP self-test | PASS | GitLab pipeline 2952846826, job 17216137522 (`local-windows`), branch `claude/pbibench-main-95fea60` = `95fea60` exactly |
| Local build, 8 stages | PASS | The first attempt failed in core-smoke with a transient `IOException: Unable to remove the file to be replaced` (`SafeFileReplace`, file lock in %TEMP%). The re-run passed with no change. CI passed the same commit on its first run. |
| Package integrity: zip SHA256 = `SHA256SUMS.txt`, 161 MANIFEST lines match, inner = outer MANIFEST, required files, no `.pdb`, guide = repo docs, `sourceCommit` = `95fea60`, `dirtyTrackedFiles=false` | PASS | `package-check.log` |
| Confidentiality scan (pattern scan of every file in the zip, text and UTF-16) | PASS | `package-check.log`. One hit, a false positive already triaged on e7a8b06: the credential-guard test string `Password=hunter2` of the MCP self-test in `mcp/TE2Enhanced.Mcp.dll`. |
| Real-value scan: zip entries, remote evidence and harness searched for the real `sp-writer` client id, secret and tenant id (UTF-8 and UTF-16) | PASS: 172 files/entries, 0 hits | `real-value-scan.ps1`. A positive control file containing the tenant id was detected and then deleted. |
| Extracted `TabularEditor.exe` | PASS: window "Tabular Editor 2.28.0" in 5.6 s, graceful close, exit 0 | `host-launch.log` |
| Extracted `mcp/TE2Enhanced.Mcp.exe --self-test` | PASS | - |
| DAX-006 live gate, local SSAS, throwaway `tmp_te2_20261015` | PASS: cancel 2864 ms, timeout 3058 ms, database dropped | `live-gate-ssas.log` (local build of the same commit) |
| Remote XMLA, `dpf-test-pbibench-20261015`, `sp-writer` | **PASS 8/8** | `remote/` (below) |
| Power BI Desktop live gate, DAX Studio handoff | Not re-run | Both passed on e7a8b06. PR #9 does not touch these paths. |
| DPI 150/200 on a real monitor, multi-monitor, interactive Entra sign-in | NOT_RUN | Manual check / Julian's login |

## Remote XMLA re-run (21:37)

Product bytes: the CI zip `3ff961d0...dd4e7`, extracted. This is byte-identical to the vault file, which was checked by an authenticated re-download. Loaded: `TabularEditor.exe` `41b30b50...`, `PbiBench.Core.dll` `a270f7a9...`, embedded TOMWrapper `6ad5b69b...`.

Harness: `../remote-xmla/evidence/tool/Program.cs`, with one change (`remote/tool/Program.cs`). The reflection constructor workaround is gone, so every query, the timeout check and the write check run through the executor that `TryCreate(handler)` returns. Credentials come from Credential Manager `dpf-test-sp-writer` and are passed on STDIN only.

| # | Check | e7a8b06 | 95fea60 |
|---|---|---|---|
| 1 | Deploy (`Gate` present, compat 1600) | PASS | PASS |
| 2 | TE2 connects and reads over XMLA | PASS | PASS (1.5 s) |
| 3 | Remote model = fixture | PASS | PASS |
| 4 | Product executor `TryCreate(handler)` | **FAIL** (`Connection` in 1 ms) | **PASS** (Q1 completed in 1.1 s) |
| 5 | DAX oracle vs local SSAS | PASS 6/6 (reflection ctor) | PASS 6/6 (product executor), `remote/oracle-compare-remote.md` |
| 6 | 1 s timeout, client side | PASS | PASS (1.5 s, `Cancelled/Timeout`, executor reusable) |
| 7 | TE2 write (`SaveDB`, query returns 70) | PASS | PASS |
| 8 | Rollback (`SaveDB` after delete; fresh TOM sees only `Total Amount`) | PASS | PASS |

`Server.ConnectionString` still lacks `Password` (`data source, user id, provider, application name, initial catalog`). The executor now restores it from the string the handler was opened with. No propagation-lag errors occurred: the workspace is about 2 h old.

## Vault receipt

| | |
|---|---|
| Destination | `juliandatapass-group/galaxy-release-vault` (private), generic package registry |
| Package | `pbibench` version `2.28.9784.42286-95fea60` (new, immutable; package id 72678105) |
| Package page | https://gitlab.com/juliandatapass-group/galaxy-release-vault/-/packages/72678105 |
| Download base | `https://gitlab.com/api/v4/projects/87564954/packages/generic/pbibench/2.28.9784.42286-95fea60/<file>` (authenticated; anonymous request returns 401) |
| Published | 2026-10-15T21:40:04Z |
| Previous version | `2.28.9784.4682-e7a8b06` (id 72532809) unchanged. Re-download hash is still `3458fac2...799bd`. |

| File | Bytes | SHA256 (local = GitLab `file_sha256` = authenticated re-download) |
|---|---|---|
| `TE2Enhanced-95fea608-portable.zip` | 12261076 | `3ff961d0a25b4ae6bb82b69c2e71317c79623691079585e4bcb37af2d33dd4e7` |
| `SHA256SUMS.txt` | 358 | `1a1be4e1741fe00745679c109a2e63b2e72a0ef15841049d598b25d96db9e9ec` |
| `MANIFEST.txt` | 18076 | `00f716af227b4c75838150707a58e4f64915ced869ffadb903af1de9e2673449` |
| `source-identity.json` | 356 | `6244728acf5488b4dea9f7bfa1f11711802106d659511221f563db29aca1861c` |
| `qualification.json` | 2405 | `66274eec625ccab2b48f10595095be2c065a6ae55b205e5663fe0d68ecde6c88` |

```sh
curl -sSL --fail -H "PRIVATE-TOKEN: $GITLAB_TOKEN" -O \
  https://gitlab.com/api/v4/projects/87564954/packages/generic/pbibench/2.28.9784.42286-95fea60/TE2Enhanced-95fea608-portable.zip
sha256sum TE2Enhanced-95fea608-portable.zip   # 3ff961d0...dd4e7
```

## Notes

- Nothing was written outside `dpf-test-pbibench-20261015`. The test measure was added and removed again, and the workspace was left in place.
- No tenant id, client id, secret, UPN or local path appears in this folder. The scripts use `<datapass-fabric-bi>` in place of the local path.
