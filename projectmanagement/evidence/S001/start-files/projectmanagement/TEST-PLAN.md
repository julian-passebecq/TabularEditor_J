# Verification strategy and S001 test campaign

Owner: lead for required behavior; light tester for execution and evidence.
Do not read this file as a claim that the tests below already exist or pass.

## Layers and their limits

1. Neutral Core: deterministic contracts, policies, bounds, history and file rules.
2. Built-host smoke: actual compiled net48 dialogs, adapters and state transitions.
3. Controlled session/formatter/file adapters: exercise production orchestration,
   blocked calls, races, payloads and failure cleanup. These seams are S001 work.
4. Native interaction: keyboard, focus, dialogs, scaling and readable status.
5. Live Desktop/XMLA: actual provider/auth/engine behavior and editing-session
   survival. Use a disposable/synthetic model and identifiable endpoint class.
6. Upstream regressions: targeted tests for touched services and clean-checkout CI.

Developer adds focused tests with implementation and runs them at each meaningful
checkpoint. QA independently executes the completed milestone and adds adversarial
coverage where needed. Do not repeatedly run unrelated full suites without a new
change or an unresolved reason.

## Required cases

| ID | Layer / scenario | Expected result and evidence |
|---|---|---|
| T01 | Build identity and prerequisites | Record branch/HEAD/dirty manifest, SDKs/MSBuild/net48 refs, package pins, stage exit codes. Missing SDK is BLOCKED_ENV, never PASS. Full runner from fresh artifacts or clean checkout. |
| T02 | Core suite | Run canonical net10 project. Exercise existing discovery/Git/policy/navigation/graph/history contracts and new result-budget/reason checks. Keep diagnostic alternate-target runs separate. |
| T03 | Host/upstream/CI | Build host before harness, run actual assembly, record artifact hashes. Run relevant available upstream tests; list exact blocked assemblies/reasons. CI trigger review includes branches, product/tests/packages/build-script inputs; remote run requires URL plus tested SHA. |
| T04 | Existing UI and model navigation | Quick Open, Semantic View and Workbench construct/resize/minimum/scaled smoke. Native keyboard/focus/hidden object navigation; use representative model and verify no model mutations. Simulated Scale(1.5) is not real monitor DPI evidence. |
| T05 | Production query lifecycle with controlled transport | Cancel before/during connect, before reader, blocked read, timeout/user race, repeated cancel, close while running, completion/cancel race, delayed cleanup and cancel throw. Callback never blocks UI; no duplicate terminal history, stale callback, editing-session use or leaked admission slot. Verify event/lifetime counts, not sleeps alone. |
| T06 | Diagnostics and stale results | Inject connection string/password/bearer token/private DAX sentinels into nested exception messages. None appear in generic status/history error/logs. Safe category appears. Success then failure/cancel leaves no old exportable rows and editor becomes usable after terminal cleanup. |
| T07 | Retained result limits | Row cap minus one/exact/plus one; byte estimate boundary; nulls/Unicode/large binary/oversized first cell; 256 vs 257 columns; duplicate/empty names; empty result. Accepted cells remain exact; limit reason visible; no false full-result label or unbounded app accumulation. |
| T08 | Real formatter boundary | Fresh session deny, explicit enable/revoke/restart, admin deny override, inherited telemetry=true, Workbench and actual expression/single/batch/script/CLI entry paths. Denial produces zero transport/redirect calls. Captured serialized body excludes model identifiers/metadata. Test long/short/separators/UDF comments, bad response, timeout and stale response after edits. Failure preserves text/undo. |
| T09 | Export and document errors | CSV commas/quotes/CRLF/Unicode/null/decimal/date under en-US and fr-CH or another comma-decimal culture; compare decoded fields, not merely file existence. Cancel save dialog; existing locked/read-only destination; injected partial write failure; cleanup. Destination/current result/draft survive failure. Include existing DAX document BOM, newline, size/invalid-byte, dirty undo, save/open failure, external edit/deletion, history isolation tests. |
| T10 | Native document/query user journey | Open draft, edit, undo/redo, save/save-as; cancel Save-As during replacement; recall history; select query; run; cancel; close; reopen file. Test actual dialogs, meaningful focus and status, min size at supported DPI. Unsaved draft is preserved; offline Execute disabled; no UI hang. |
| T11 | Real Power BI Desktop | Synthetic model: ROW success, invalid DAX, empty result, row limit, long query cancel/timeout, endpoint disappears, second successful query, close during execution. Record provider/Desktop versions, query fixture, requested timeout vs observed duration/UI responsiveness. Verify editor connection remains usable and model not modified by query operations. |
| T12 | Real supported XMLA/auth | Repeat relevant T11 using available approved XMLA test endpoint; valid and invalid/auth-expired connection, catalog handling, reconnect, cancellation, editor session continuity. Identify auth method without secrets. Desktop evidence alone cannot establish service-token cloning. |
| T13 | Readiness and source audit | Trace UI -> Core -> AMO/formatter/file boundary -> UI including cleanup; verify no policy bypass and ledger entries for upstream changes. Check docs against behavior, one-result-set limitation, retained-memory/server-work distinction and exact known blocks. |

Use deterministic synchronization (events/task completion sources) for race tests.
Use bounded watchdogs so a hang fails with useful evidence. For the controlled
transport, hold cancellation completion deliberately while proving the UI handles
another message; avoid a fragile hardware-speed assertion as the sole oracle.
For live tests record observed elapsed time and responsiveness rather than
promising a universal deadline. Capture model fingerprints/dirty state before and
after query-only tests. Perform any edit/undo/save check only on a disposable model.

Use synthetic DAX and anonymized paths in retained evidence. Do not save access
tokens or real query results in the repo. A user may supply a private endpoint
through the normal application; reports need its class and a neutral label only.

## Commands and build order

Run from the repository root. These commands describe current project structure;
the S001-A script must package them with prerequisite and exit-code handling.
Do not run them in parallel where project outputs/assets overlap.

```powershell
# Requires supported .NET 10 SDK, NuGet, VS MSBuild and .NET Framework 4.8 refs.
nuget restore TabularEditor.sln -NonInteractive
dotnet restore PbiBench.Core/PbiBench.Core.csproj --nologo
msbuild AntlrGrammars\AntlrGrammars.csproj /m /t:Rebuild /p:Configuration=Debug /p:Platform=AnyCPU /verbosity:minimal
msbuild TabularEditor\TabularEditor.csproj /m /p:Configuration=Release /p:Platform=AnyCPU /verbosity:minimal
dotnet run --project PbiBench.Core.Smoke/PbiBench.Core.Smoke.csproj -c Release
dotnet build PbiBench.Host.Smoke/PbiBench.Host.Smoke.csproj -c Release --nologo
& ./PbiBench.Host.Smoke/bin/Release/net48/PbiBench.Host.Smoke.exe
```

The future primary command after S001-A is:

```powershell
# Planned; not present at management setup time.
./scripts/Verify-PbiBench.ps1
```

Stop dependent stages on failure and record the exit code immediately. In a
shell without MSBuild on PATH, resolve it using VS Installer `vswhere.exe`.
The baseline host build used VS2022 Build Tools with process-local
`MSBuildSDKsPath=C:\Program Files\dotnet\sdk\9.0.101\Sdks` and
`MSBuildEnableWorkloadResolver=false`. That enables the host build on this machine;
it does not make net10 Core smoke supported. Do not commit machine-specific paths
into shared build settings. Record any diagnostic alternate harness exactly and
restore affected build assets afterward if a target override changed them.

## Evidence and triage rules

Every result records test ID, source identity, command/steps, environment, outcome,
expected/actual, exit code where applicable, log/screenshot/fixture reference and
limitations. Prefer compact Markdown/JSON reports; large raw logs can be external
artifacts with paths/hashes. Keep logs free of private data before linking them.

FAIL means behavior differs from a specified oracle. BLOCKED_ENV means the test
could not establish behavior because a prerequisite is unavailable. NOT_RUN is
an unexecuted case without a demonstrated block. Separate flaky harness issues
from product failures and preserve first-failure evidence.

T11/T12 missing environments should produce a single manual-session checklist,
not repeated requests during coding. QA can finish the offline report and hand
the remaining gate to the lead. Full sprint acceptance is the lead's decision;
QA cannot waive required cases or substitute source inspection for live evidence.
