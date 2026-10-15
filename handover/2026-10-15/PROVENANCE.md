# Portable package provenance and vault receipt (2026-10-15)

Question: the Finition QA (`handover/2026-10-13/QA_FINITION_REPORT.md`, YELLOW) ran on `be3a6a0`, but the CI package that was kept is from `7a2a5f5`, and `master` has since moved to `e7a8b06` (PR #5). Which build is qualified, and which one goes to the vault?

Answer: the vault holds the CI build of `e7a8b06` (current `master`), re-qualified locally on 2026-10-15. The `7a2a5f5` artifact was not published, because `master` moved past it with product changes.

## 1. be3a6a0 -> 7a2a5f5 (QA source -> kept CI package)

`git diff --stat be3a6a0 7a2a5f5`: two files.

| File | In the zip? |
|---|---|
| `docs/pbibench/GETTING_STARTED.md` (shortcut wording, exact menu names; the two QA doc defects) | Yes, copied as `GETTING-STARTED.md` by `Scripts/ci/Package-PbiBench.ps1` |
| `handover/2026-10-13/QA_FINITION_REPORT.md` | No |

So the package bytes do depend on that range, through the guide only. Comparison of the two CI artifacts (GitLab jobs 17149422617 for `be3a6a0` and 17149771477 for `7a2a5f5`):
- Same 161-entry file list in `MANIFEST.txt`.
- 7 files differ: `GETTING-STARTED.md` (source change), `README-TE2Enhanced.txt` (contains the commit id), and 5 rebuilt binaries (`TabularEditor.exe`, `PbiBench.Core.dll`, `mcp/PbiBench.Core.dll`, `mcp/TE2Enhanced.Mcp.dll`, `mcp/TE2Enhanced.Mcp.exe`). No source of those binaries changed. Their hashes differ only because each build stamps a new timestamp and version (`source-identity.json`: "Build timestamps make binaries non-reproducible").
- The QA report's zip (`1bbb8195...`) was a local build of `be3a6a0`. It is neither CI artifact (`c2b21f07...` for `be3a6a0`, `bc8ddb7b...` for `7a2a5f5`). QA therefore qualified the source, not one exact zip.

The relevant gate for that range is guide accuracy (QA check 7). Its two defects are what the range fixes. The guide shipped in the published zip is byte-equal to `docs/pbibench/GETTING_STARTED.md` at `e7a8b06`, which still contains those fixes.

## 2. 7a2a5f5 -> e7a8b06 (current master)

`master` = `d646d37` (PR #4, finition) then `e7a8b06` (PR #5, desktop gates). PR #5 changes product code that ships in the zip:
- `PbiBench.Core/Dax/DaxStudioHandoff.cs`: DAX Studio arguments `/server=` `/database=`.
- `TabularEditor/PbiBench/Dax/PbiBenchAmoDaxQueryExecutor.cs`: cancel and timeout end the query session (about 3 s instead of 11 s).
- `TabularEditor/PbiBench/Dax/PbiBenchDaxWorkbenchForm.Handoff.cs`.

So the `7a2a5f5` package no longer matches `master`. The PR #5 branch tree is identical to `e7a8b06` (`git diff origin/claude/te2-desktop-gates-2026-10-15 origin/master` is empty), so its desktop-gate evidence (`DESKTOP_GATES.md`) applies to this exact tree. PR #5 had no GitLab pipeline: its branch name does not match the CI rules. This pass therefore built `e7a8b06` through CI and re-ran the gates.

## 3. Build and gates for e7a8b06

| Gate | Result | Evidence |
|---|---|---|
| CI build: `Verify-PbiBench.ps1` 8 stages, package, MCP self-test on the runner's runtime | PASS | GitLab pipeline 2948872778, job 17187088432 (`local-windows`), branch `claude/pbibench-main-e7a8b06` = `e7a8b06` exactly |
| Local build, 8 stages | PASS | `vault-evidence/local-verify-summary.json` |
| Package integrity: zip SHA256 = `SHA256SUMS.txt`; 161 MANIFEST lines match the extracted files; inner = outer MANIFEST; required files present; no `.pdb`; `sourceCommit` = `e7a8b06`, `dirtyTrackedFiles=false` | PASS | `vault-evidence/package-check.log` |
| Confidentiality scan of every file in the zip (text and binary strings) | PASS | `vault-evidence/package-check.log` |
| Extracted `TabularEditor.exe` | PASS: window "Tabular Editor 2.28.0" in 3.7 s, graceful close, exit 0 | this file |
| Extracted `mcp/TE2Enhanced.Mcp.exe --self-test` | PASS | this file |
| DAX-006 live gate, local SSAS `localhost\MSSQLSERVERTABU`, throwaway `tmp_te2_20261015` | PASS: cancel 2898 ms, timeout 3137 ms; database dropped (`stillExists=False`, no `tmp_*` left) | `vault-evidence/live-gate-ssas.log` |
| Power BI Desktop live gate (copy of `fixture/Gate.pbip`) | PASS: TE2 wrote `TE2 Gate Double`, Total=35, Double=70, cancel 754 ms | `vault-evidence/desktop-gate-live.log` |
| DAX Studio 3.0.11 started with TE2's handoff arguments | PASS: connected to Desktop (`localhost:<port>`, database `Gate`, table `Sales`), no Connect dialog | `vault-evidence/daxstudio-handoff-e7a8b06.png` |
| DPI 150/200 on a real monitor, multi-monitor | NOT_RUN (manual) | - |
| Remote XMLA / Entra | NOT_RUN (cloud login) | - |

Verdict: YELLOW, no RED. It is the same verdict as the Finition QA, and the Power BI Desktop and DAX Studio gates have moved from BLOCKED_ENV to PASS.

Not re-run on this build: the Desktop disk round trip (save in Desktop, then reload the TMDL in TE2). It passed in PR #5 on the identical tree.

Confidentiality scan triage: 3 pattern hits, all false positives:
- locale tags such as `sk-SK` in `msalruntime*.dll`;
- the masked `password=********` log template in `Microsoft.AnalysisServices.Core.dll`;
- the credential-guard test strings (`Password=hunter2`, `client_secret=zzz`) of the MCP self-test in `TE2Enhanced.Mcp.dll`.

The scan found no tokens, keys, user-profile or `D:\PROJ` paths, personal email or FOIL/client data.

Cleanup: Power BI Desktop (`PBIDesktop` and its `msmdsrv`), started only for this run, was stopped by process id. No auto-recovery file was created, and its AS workspace folder and the fixture copy were removed. DAX Studio was closed through its own window. No other process was touched.

## 4. Vault receipt

| | |
|---|---|
| Destination | `juliandatapass-group/galaxy-release-vault` (private), generic package registry |
| Package | `pbibench` version `2.28.9784.4682-e7a8b06` (new, immutable; no `pbibench` package existed before) |
| Package page | https://gitlab.com/juliandatapass-group/galaxy-release-vault/-/packages/72532809 |
| Download base | `https://gitlab.com/api/v4/projects/87564954/packages/generic/pbibench/2.28.9784.4682-e7a8b06/<file>` (authenticated; anonymous request returns 401) |
| Published | 2026-10-15T00:44:58Z |

| File | Bytes | SHA256 (upload = authenticated re-download) |
|---|---|---|
| `TE2Enhanced-e7a8b067-portable.zip` | 12260486 | `3458fac2521d1154a0a4d11b43c9d5248e2ecc48523ea7275739fca8a46799bd` |
| `SHA256SUMS.txt` | 358 | `7050a66765e69cfd4b48657c599f416abd22ca8ce2ce0f452a906c4cbef5a7d7` |
| `MANIFEST.txt` | 18076 | `80f3d3c4de996348f0af0971d3d51a4c47cbb74fc25ed89019006534e5bc5e12` |
| `source-identity.json` | 355 | `52b17e2167d48b212cb42fe3387d119fd5dd1901f105c380efc959600324b61c` |
| `qualification.json` | 1752 | `e8080604f7ff8709ea3a094dbecf6fff35f101a381df41bed69a0a96f815e13d` |

The GitLab-reported `file_sha256` of each upload, the local SHA256 before upload and the SHA256 of an authenticated re-download all match. The CI job artifact (job 17187088432) still expires after 30 days; the vault copy does not.

Re-download and check:
```sh
curl -sS --fail -H "PRIVATE-TOKEN: $GITLAB_TOKEN" -O \
  https://gitlab.com/api/v4/projects/87564954/packages/generic/pbibench/2.28.9784.4682-e7a8b06/TE2Enhanced-e7a8b067-portable.zip
sha256sum TE2Enhanced-e7a8b067-portable.zip   # 3458fac2...799bd
```
