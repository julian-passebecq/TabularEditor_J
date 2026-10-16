# PbiBench cancel/timeout: the server query really stops (2026-10-16)

Defect (pilot PR #7, check 5, FAIL): after a cancel or a timeout, the DAX executor gave control back to the client
before the server had stopped the query. The engine kept the command in `DISCOVER_COMMANDS` for 3-4.5 s after
the client returned.

Verdict: **fixed.** The run now ends only after the server is seen to stop the query, or, after a 30 s wait,
it says the server is still computing. The result carries `ServerStop` (`Confirmed` / `StillRunning` / `Unknown`)
and `ServerStopMilliseconds`, and the message states what the server did.

All runs: local SSAS 2022 (`localhost`, 16.0.42.229, Tabular), throwaway database `tmp_pbibench-cancel_20261016`.
It was created and dropped by each run. The instance had 0 databases before and after, and nothing else was opened.

## Root cause (measured, `evidence/method-matrix/`)

| Query | Cancel method sent at +0.7-1.5 s | Server command end |
|---|---|---|
| Formula-engine bound (CROSSJOIN 6000x6000, FILTER), 5.9 s uncancelled | none / `Cancel` by SessionID+CancelAssociated / by SPID / by SPID+CancelAssociated | ran to its end every time: CPU 5.8-6.1 s ≈ the whole query. Session `SESSION_STATUS=3` (cancelling) is set at once, but the engine does not check it during formula-engine work. |
| Same, three EVALUATE statements (16 s) | SessionID+CancelAssociated | all three statements ran (CPU 16.1 s): there was no check between statements either |
| Storage-engine bound (2000 `CALCULATE` scans of a 3M-row table), > 6 min uncancelled | SPID+CancelAssociated | **stopped 327 ms after the cancel** |

So the engine honours a cancel at storage-engine requests and ignores it during pure formula-engine work. No
client-side method can stop the formula-engine work faster. The client defect was that the executor returned
`Cancelled` as soon as the session abort released the blocked call, so it said "cancelled" while the engine was
still computing.

## Fix (`TabularEditor/PbiBench/Dax/PbiBenchAmoDaxQueryExecutor.cs`)

- The cancel path is unchanged: the command cancel first, then after 2 s the session is ended once
  (`Cancel` SessionID + CancelAssociated). `Abort` now reads the query session's SPID from `DISCOVER_SESSIONS`
  before it ends the session.
- New `IDaxQuerySession.WaitForServerStop`. After a cancel or timeout, once the blocked call has returned and
  before `Disconnect`, while executor admission is still held (no new query overlaps), a second short-lived
  connection polls `$SYSTEM.DISCOVER_COMMANDS` every 200 ms. It waits until no row of that SPID lacks a
  `COMMAND_END_TIME`, for up to 30 s (`DefaultServerStopWaitMilliseconds`). It is read-only.
  - It returns `Confirmed` when no row of the SPID is still running.
  - It returns `StillRunning` when the 30 s wait ends first.
  - It returns `Unknown` when the SPID is unknown, a DMV is refused (for example by a non-admin XMLA endpoint)
    or the connection fails. It never guesses.
- `PbiBench.Core`: `DaxServerStopState` enum and two result properties. There are no new dependencies.
- Workbench: the status text on cancel now reads "waiting for the server to stop the query (up to 30 s)". The
  Cancelled status shows the message, for example "Query cancelled; the server stopped the query 671 ms after
  the cancel."
- Independent review (Sonnet) flagged that "session absent" was counted as stopped. That is fixed: the stop is
  now judged only from the SPID's command rows, and an unknown SPID gives `Unknown`.

## Proof: before/after probe with the product executor (`evidence/probe/`)

The same probe drives `PbiBenchAmoDaxQueryExecutor` from each build. Every 100 ms, a separate connection samples
the query's `DISCOVER_COMMANDS` row (tagged by a unique result-column name) and the msmdsrv `% Processor Time`
(one core is about 4-5 % on this PC). `COMMAND_END_TIME` is the server's own UTC end time.

| Case | Before: master 4c4523b (= vault 95fea60 executor) | After: this branch |
|---|---|---|
| FE cancel@700 ms | client 2766 ms; **server ended 3610 ms after the client returned**; CPU in the 2 s after return 4.0 % | client 6901 ms, `Confirmed` 6183 ms after cancel; server ended **47 ms before** the client returned; CPU after 0.0 % |
| FE timeout 1 s | client 3091 ms; **server ended 3677 ms after**; CPU after 3.6 % | client 6307 ms, `Confirmed`; server ended **208 ms before**; CPU after 0.1 % |
| SE cancel@700 ms | client 871 ms; row gone at +1024 ms (after the client); CPU after 0.0 % | client 1386 ms, `Confirmed` 671 ms after cancel; CPU after 0.0 % |
| SE timeout 1 s | client 1067 ms; row gone at +1240 ms (after the client); CPU after 0.0 % | client 1877 ms, `Confirmed` 855 ms after cancel; CPU after 0.0 % |
| Executor reuse after each case | Completed | Completed |

Logs: `evidence/probe-before-4c4523b.log`, `evidence/probe-after-final.log` (each 100 ms sample listed).

## Gates

| Gate | Result | Evidence |
|---|---|---|
| `Verify-PbiBench.ps1` 8 stages, local | PASS. The first attempt hit the known transient `SafeFileReplace` IOException in core-smoke; the re-run passed. | - |
| Controlled smoke `QueryLifecycleSmoke.ServerStop` | PASS. Confirmed/StillRunning/Unknown × user/timeout: the wait is called exactly once, before Disconnect, while admission is held (an overlapping query is refused), with the configured bound; the message is truthful; the executor is reusable; a throwing check gives Unknown with cleanup; a completed run never waits. | host-smoke-run |
| Live gate DAX-006 (`Run-LiveGate.ps1 -Server localhost -Database tmp_pbibench-cancel_20261016`) | PASS. SE cancel: stop 799 ms after cancel; SE timeout: 228 ms; FE cancel: 8778 ms; FE timeout: 8056 ms. Each was re-checked from the gate's own admin connection: no gate command still running. The editor connection survives; reuse works. | `evidence/live-gate-ssas.log` |
| GitLab CI (local runner `local-windows`) | PASS: pipeline 2956429359 / job 17242672990 on 7ad5bea; master 45d2ff4: pipeline 2956440013 / job 17242760608. GitHub Actions 4/4. | PR #11 |
| Power BI Desktop gate (`DesktopGateSmoke`, now asserts `Confirmed` and < 40 s) | NOT_RUN: the GUI slot is held by another lane. Desktop answered a cancel in 0.7 s on e7a8b06. | Light QA |
| Remote XMLA (Fabric) | NOT_RUN, by design: no Fabric access in this lane. A non-admin DMV refusal gives `Unknown`, never a hang. | Light QA |

## Limits that remain (truthful)

- A pure formula-engine query keeps running on the server until it finishes. The client now waits for it, up to
  30 s, then reports `StillRunning`. This is an engine limit, not a client one.
- On endpoints that refuse `DISCOVER_SESSIONS`/`DISCOVER_COMMANDS`, the result is `Unknown` after a cancel.

Merged as `45d2ff4` (PR #11). Vault build and rollback proof: `../vault-45d2ff4/VAULT_45d2ff4.md`.
