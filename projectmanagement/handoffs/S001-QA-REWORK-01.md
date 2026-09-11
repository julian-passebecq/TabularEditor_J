# S001-QA-REWORK-01 — independent correction verification

State: READY_FOR_LEAD_REVIEW. Date: 2026-09-08. Owner: independent tester.
BUG-001 and BUG-002 are QA_VERIFIED for the tested correction scope; S001 is not accepted.

## Lead briefing

The corrected source matched all 51 delivery-manifest entries. A fresh Release
host rebuild, standard built-host smoke including T14/T15 and affected T07/T08/T09,
and additional QA checks passed. No reproducible correction defect requires a
further developer batch. Return to the lead for focused acceptance review.
Prior QA, developer and lead evidence were preserved. The user's open app was not
interacted with, closed or restarted; tests created their own synthetic fixtures.

## Identity and build

Branch codex/stabilize-pbibench-v02; HEAD a7317e7ff1bbdb8949b7e6d82b6277b6e22300de,
plus dirty delivery. [Start status](../evidence/S001/qa-rework-01/start-status.txt),
[51-file comparison](../evidence/S001/qa-rework-01/source-verification.json),
[post-test comparison](../evidence/S001/qa-rework-01/post-test-source.json).
No production or delivered-test edits, commit, staging, push, reset or branch switch.
QA added an isolated test project and evidence, then updated management records.

[Artifact hashes](../evidence/S001/qa-rework-01/built-artifacts.json) record the rebuilt
host, standard harness and QA harness with their copied host assemblies. Both
harnesses reference the rebuilt production assemblies, not substituted source.
Environment remains Windows/net48, SDK9.0.101 and VS2022 Build Tools; see the
[SDK list](../evidence/S001/qa-rework-01/build/sdks.txt). No packages/targets changed.

Commands from repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Verify-PbiBench.ps1 -HostOnlyDiagnostic -NuGetPath D:/PROJ/powerbi_enhanced_dev/.tools/nuget.exe -EvidenceDirectory D:/PROJ/TabularEditor_J/projectmanagement/evidence/S001/qa-rework-01/build
dotnet build projectmanagement/evidence/S001/qa-rework-01/QaRework.csproj -c Release --nologo
& ./projectmanagement/evidence/S001/qa-rework-01/bin/Release/net48/PbiBench.Host.Smoke.exe
git diff --check
```

The runner's [stage summary](../evidence/S001/qa-rework-01/build/summary.json) records
restore, ANTLR Debug rebuild, host Release rebuild, harness build and smoke run
all exit0. Core is explicitly NOT_RUN_DIAGNOSTIC. The surrounding Start-Process
-Wait wrapper did not return promptly after the runner completed; its overall
exit is not used as evidence. Completed stage logs establish the build/test results.
No user process was terminated to release that wait.

## Results

| Case | Expected / actual | Evidence |
|---|---|---|
| T14 exact lead examples | PASS: Double 1.0000000000000002 and Single 1.00000012 survive CSV parse; actual grid callback agrees | [standard smoke](../evidence/S001/qa-rework-01/build/host-smoke-run.log), Rework01Smoke assertions inspected |
| T14 broader numeric fidelity | PASS: 8,260 seeded bit-pattern and adjacent-to-one typed values round-trip through actual CSV Write under en-US/fr-CH, parsed independently as Double/Single | [QA final run](../evidence/S001/qa-rework-01/extended-run-final.log), [QA source](../evidence/S001/qa-rework-01/QaRework.cs). Numeric equality, not bit identity or same-helper oracle |
| T14 boundaries/policy | PASS: subnormal, maximum, negative, NaN/infinities and signed-zero checks; fixed spelling for reported values, NaN/Infinity/-Infinity and canonical 0 | Standard Rework01Smoke; finite typed values preserved, signed-zero bits intentionally not preserved |
| T15 document replacement | PASS: held request for A rejected after equal-text B, same-path reopen, empty New and history recall; text/path/dirty/CanUndo unchanged after release | Standard smoke with real recursively created child handles, recording transport and bounded events |
| T15 no replacement | PASS: failed/cancelled Open keeps generation and valid pending formatting; Save/Save As keeps buffer generation, successful format dirties it and one undo restores text | Standard smoke |
| T15 additional cancellation | PASS: dirty Cancel-New, dirty Cancel-Open, cancelled Save As before New and failed save before New keep generation; pending same-buffer result remains applicable and undoable | [extended fixture](../evidence/S001/qa-rework-01/ExtendedRework.cs), QA final run |
| T15 expression contexts | PASS: equal-text object/property/handler away-and-back invalidates pending result; unchanged context applies | Standard model-bound controller fixture, real expression handler |
| T15 additional expression cases | PASS: one-way object/property/handler changes also discard pending response | QA extended fixture; handler change uses actual Handler setter, not live model reopening |
| Affected T07/T08/T09 | PASS: retained limits, consent/telemetry payload/script/CLI/UDF/stale/undo, CSV culture/file failure, documents/history and earlier standard host tests | Standard smoke exit0; recording transport only, no external formatter call |
| T13 correction source | SOURCE_REVIEW completed; diff whitespace check PASS exit0 | Explicit G17/G9 precedes generic conversion; DisplayDocument increments generation; formatting checks generation and text; handler/object/property transitions increment context and FormMain checks controller/context; upstream ledger includes edits |

Additional QA build/run final exit codes both 0:
[results](../evidence/S001/qa-rework-01/extended-results-final.json).
The seeded corpus supplements fixed examples; it is not exhaustive IEEE coverage.
Existing decimal/date/text/null and atomic-write regressions passed in the standard
suite. The expression success oracle checks editor application; this campaign
adds no claim about a complete native edit/commit/undo journey.

## Fixture failure and correction

The first extended run passed all 8,260 numeric cases but failed the new
cancelsave generation assertion. The QA fixture still had an existing file path,
so Save correctly bypassed Save As and New succeeded. This was an incorrect QA
setup, not a product failure. The corrected fixture starts an untitled document
before the dirty cancelled/failed-save scenarios. The final run passed without
production changes or weakened expected behavior. [First failure preserved](../evidence/S001/qa-rework-01/extended-run-initial-fixture-error.log).
ExtendedRework.cs is an explicitly adapted copy of the delivered Rework01Smoke;
QaRework.cs adds independent typed numeric oracles. Both are isolated in qa-rework-01/.

## Remaining gates and routing

Lead retains acceptance authority. The existing in-process best-effort timeout
architecture remains as decided in S001-DECISION; live timeout/authentication,
Desktop/XMLA fixtures, canonical net10, legacy references, remote clean-checkout
CI and remaining native journey/DPI gates stay open. Those wider lanes were not
rerun for this correction and prior results are not presented as fresh evidence.
The lead's limited native Quick Open evidence is preserved at its original scope.
OBS-001 remains open, low severity; no replacement IOException occurred here, but
non-reproduction is not a root-cause resolution.

Next prompt:

> Act as technical lead. Read STATUS, the appended S001-REWORK-01 developer handoff and S001-QA-REWORK-01. Review the corrected numeric conversion and document/expression context guards against BUG-001/BUG-002. Decide focused acceptance in S001-DECISION, retaining existing live/canonical/native/CI gates. Update backlog and authorize a next sprint only through your recorded decision. QA reports both corrections verified, with no new reproducible product defect.
