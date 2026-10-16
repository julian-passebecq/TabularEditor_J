# PbiBench vault build 45d2ff4 (2026-10-16)

`master` = `45d2ff4` (PR #11: cancel and timeout return only once the SSAS query has really stopped;
`DaxQueryResult.ServerStop`). This build replaces `95fea60` as the current vault build. `95fea60` and `e7a8b06`
stay in the vault unchanged. Fix details and root cause: `../cancel-server-stop/CANCEL_SERVER_STOP.md`.

Verdict: **YELLOW, no RED.** The last functional FAIL (pilot check 5: the client was released before the server
stopped) is PASS on the published bytes.

## Gates

| Gate | Result | Evidence |
|---|---|---|
| CI: `Verify-PbiBench.ps1` 8 stages, package, MCP self-test | PASS | GitLab pipeline 2956440013, job 17242760608 (`local-windows`), branch `claude/pbibench-main-45d2ff4` = `45d2ff4` |
| PR CI | PASS | GitLab job 17242672990 on 7ad5bea; GitHub Actions 4/4 |
| Package integrity: zip SHA256 = `SHA256SUMS.txt`, 161 MANIFEST lines match, inner = outer MANIFEST, no `.pdb`, guide = repo docs, `sourceCommit` = `45d2ff4`, `dirtyTrackedFiles=false` | PASS | `package-check.log` |
| Confidentiality scan: every zip entry, raw and UTF-16-stripped, for tokens, keys, connection secrets, user-profile and dev paths, personal emails, FOIL data, transcript markers | PASS | `package-check.log`, `package_check.py`. A positive control with an injected `Password=` was detected. The known false positives are the MCP self-test strings `Password=hunter2` and `PWD=abc` (`TE2Enhanced.Mcp/SelfTest.cs`). |
| Extracted `mcp/TE2Enhanced.Mcp.exe --self-test` | PASS | `mcp-self-test-package.log` |
| Cancel/timeout server stop on the **package bytes** (extracted `TabularEditor.exe` `312648bd…`, `PbiBench.Core.dll` `2d4fe418…`; TOMWrapper from a local build of the same tree), local SSAS, `tmp_pbibench-cancel_20261016` | PASS. Storage-engine (SE) and formula-engine (FE) queries, each with a cancel and a timeout. All four report `ServerStop=Confirmed`. Server `COMMAND_END_TIME` came before the client return: SE cancel −46 ms, FE cancel −205 ms, FE timeout −144 ms. For SE timeout the probe sampled no end time: the row was gone 148 ms after the return, while the product had confirmed the stop 211 ms after the cancel. msmdsrv CPU was 0.0 % in the 2 s after each return. The executor was reusable each time. The database was dropped and the instance has 0 databases. | `probe-package-45d2ff4.log` |
| Live gate DAX-006 (local build of the same tree) | PASS | `../cancel-server-stop/evidence/live-gate-ssas.log` |
| Extracted host window launch | NOT_RUN: no desktop GUI in this lane. CI host-smoke loads the host. | Light QA |
| Power BI Desktop live gate, remote XMLA re-run, DPI/multi-monitor, Entra sign-in | NOT_RUN | Light QA / Julian's login |

## Rollback to 95fea60 (proven 2026-10-16)

- Authenticated re-download of `pbibench 2.28.9784.42286-95fea60` / `TE2Enhanced-95fea608-portable.zip`:
  SHA256 `3ff961d0a25b4ae6bb82b69c2e71317c79623691079585e4bcb37af2d33dd4e7`, equal to the 2026-10-15 receipt. An
  anonymous request returns 401.
- Extracted and run:
  - MCP self-test: PASS (`mcp-self-test-rollback-95fea60.log`).
  - The same probe on its `TabularEditor.exe` (`41b30b50…`, equal to the 95fea60 receipt) gives the old
    behaviour, as expected. FE: the server ended 3325 ms and 12272 ms after the client returned, and
    `ServerStop` is not in that build. The executor was reusable each time (`probe-rollback-95fea60.log`).
- To roll back, download the 95fea60 zip, check the hash above and replace the extracted folder. There is no
  installer and no registry state.

## Vault receipt

| | |
|---|---|
| Destination | `juliandatapass-group/galaxy-release-vault` (private), generic package registry |
| Package | `pbibench` version `2.28.9785.35101-45d2ff4` (new, immutable; package id 72818606) |
| Package page | https://gitlab.com/juliandatapass-group/galaxy-release-vault/-/packages/72818606 |
| Download base | `https://gitlab.com/api/v4/projects/87564954/packages/generic/pbibench/2.28.9785.35101-45d2ff4/<file>` (authenticated; an anonymous request returns 401) |
| Published | 2026-10-16T17:34:12Z |
| Other versions | `2.28.9784.42286-95fea60` (72678105) and `2.28.9784.4682-e7a8b06` (72532809), unchanged |

| File | Bytes | SHA256 (local = GitLab `file_sha256` = authenticated re-download) |
|---|---|---|
| `TE2Enhanced-45d2ff4b-portable.zip` | 12262561 | `e12eb6e018670e4191d597ed8c49277771de54e1953d00309ce4646ee2a7a6b4` |
| `SHA256SUMS.txt` | 358 | `9f4a44db4a624e87c2324ec7b39f288984a512758af85a56f7cb2f7fd76d02d1` |
| `MANIFEST.txt` | 18076 | `2ca8a729f33c3b0ffdbd61d69958c41a78e1b322223dfa4db6a939c88f0fcb95` |
| `source-identity.json` | 356 | `80f0759ab22e043a42ddef28b82d2e3c6ba48814289428ce1150e0f912ac6f7d` |
| `qualification.json` | 2409 | `37693cb826c789697f9bce9b321941e808e61b3943d623330107e1d9b65ee332` |

```sh
curl -sSL --fail -H "PRIVATE-TOKEN: $GITLAB_TOKEN" -O \
  https://gitlab.com/api/v4/projects/87564954/packages/generic/pbibench/2.28.9785.35101-45d2ff4/TE2Enhanced-45d2ff4b-portable.zip
sha256sum TE2Enhanced-45d2ff4b-portable.zip   # e12eb6e0...a7a6b4
```

## For Light QA

- Run `Scripts/live/Run-LiveGate.ps1 -Server <tabular instance> -Database tmp_pbibench-<lane>_<yyyymmdd>`.
- Run the Desktop gate. It now asserts `ServerStop=Confirmed` and a cancel within 40 s.
- On a Fabric XMLA endpoint, check whether a non-admin gets `Unknown` instead of `Confirmed` after a cancel.
- A pure formula-engine query now holds the workbench in "Cancellation requested" until the engine finishes, up
  to 30 s. This is intended: the engine does not interrupt formula-engine work.
