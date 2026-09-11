# S001 technical-lead decision

State: **REWORK_REQUIRED**. Date: 2026-09-08. Owner: technical lead.

The implementation has made substantial progress, and the offline QA evidence
is useful. Two additional correctness defects prevent accepting the complete
offline milestone. Keep S001 active and execute [one correction batch](../sprints/S001-REWORK-01.md).
Do not start the DAX editor feature sprint yet.

## Reviewed identity and scope

Reviewed `codex/stabilize-pbibench-v02`, HEAD
`a7317e7ff1bbdb8949b7e6d82b6277b6e22300de` plus delivered uncommitted files.
At lead intake, all 47 developer-manifest paths were compared: 43 matched, and
the four expected QA-updated management records differed (BACKLOG, STATUS,
REGISTERS and S001-QA). No product-source mismatch was found. The launched
`TabularEditor/bin/Release/TabularEditor.exe` matched QA's SHA-256
`40744884C0CD1B08B5E9772201E91AE6F86980C9DD348646A59196659E49987D`.

Reviewed actual query orchestration/session adapter, request/history/retention
contracts, Workbench execution/documents/formatting/export, shared formatter
transport/consent, upstream expression and script paths, relevant surrounding TE2
expression/model handling, build runner/CI, changed documentation and test oracles.
Pre-existing stabilization code is part of acceptance. This is not an exhaustive
audit of unchanged upstream TE2 internals or external AMO internals.

Read [QA report](../handoffs/S001-QA.md), [developer handoff](../handoffs/S001-DEVELOPER.md)
and [provider questions](../handoffs/S001-LEAD-QUESTIONS.md). Existing QA results
remain valid for the cases actually exercised. Its statement that no defect was
found describes that campaign; it does not supersede the findings below.

## Findings requiring correction

### BUG-001 — P1: floating-point result values lose precision

`PbiBench.Core/Dax/DaxResultRetention.cs:32` uses
`Convert.ToString(value, CultureInfo.InvariantCulture)` for boxed Single/Double.
On the actual net48 host this uses a default precision that can change the numeric
value when read back. `DaxCsvExport.Write` uses this helper, as does grid rendering.

Actual source/assembly probe, under both en-US and fr-CH:

```text
Double: 1.0000000000000002 -> "1" -> a different value
Single: 1.00000012         -> "1" -> a different value
CSV data row: "1","1"
```

This violates the sprint's intact-result/data-export contract. Invariant culture
does not imply round-trip precision. The previous decimal/date tests did not cover
the floating-point conversion path. Fix the shared formatter with explicit
round-trip numeric precision (G17 Double / G9 Single on net48), preserving existing
decimal/integer/text/date handling. See T14 and the correction packet.

### BUG-002 — P2: formatting response crosses document replacement

`PbiBenchDaxWorkbenchForm.Formatting.cs:18,46,60` identifies request freshness only
through TextChanged revision and consent. `...Documents.cs:104` replaces the
logical document by assigning text and clearing undo. It has no document-generation
change. Assigning identical text is insufficient evidence of document identity.

Reproduction with the real built Workbench and controlled formatter:

1. Open `same-a.dax`, start formatting and hold the response.
2. Open `same-b.dax` containing identical text while the first request is pending.
3. Release the response associated with A.
4. B receives the formatting result and becomes dirty, although formatting was
   requested for A. The observed text revision stayed 0 -> 0 across replacement.

This is an isolated actual-host fixture result, not a claim that this sequence
was exercised in the visible native window. Existing tests changed text while
formatting; they missed replacement with identical content. Track logical document
generation separately from content revision and discard responses from a replaced
document. Do not use the reused `_document` object's reference or path equality as
the generation. See T15 and the correction packet.

Evidence for both: [probe source](../evidence/S001/lead/LeadReview.cs),
[project](../evidence/S001/lead/LeadReview.csproj),
[build log](../evidence/S001/lead/probe-build.log),
[run log](../evidence/S001/lead/probe-run.log). Probe build exit 0, zero warnings;
run exit 1 with five failing assertions grouped into these **two** defects.
The probe references the delivered assemblies; it does not replace product logic.
No external formatting request was made.

## Architecture decisions

**R-001 design decision: retain in-process dedicated AMO sessions for this stage.**
The executor snapshots requests, owns one session and reader, signals cancellation
on a worker, seals scheduling before joining cancellation, then disposes resources
before releasing admission. This addresses the previous UI cancellation call and
abandoned-session risks. Retrying cancel on that same owned session covers the
command-start race without creating an unbounded worker list.

The timeout/cleanup limitation is accepted as an explicit initial design limit,
not a proven live guarantee: a stuck Connect/Cancel/reader cleanup/Disconnect/
Dispose call can occupy the run indefinitely. No thread abort, early release of
admission, or speculative transport upgrade is approved. Keep the draft open and
the state truthful. Revisit worker-process isolation only if live evidence shows
an unacceptable recoverability problem or a hard wall-clock deadline becomes an
explicit product requirement. This decision does not close T11/T12.

**Authentication remains an acceptance gate, not an inferred capability.**
Connection-string parsing proves timeout settings can be represented; it does not
prove token renewal, external AccessToken cloning or actual server timing. The
adapter captures string/catalog only. Do not advertise all XMLA authentication
profiles as supported. Use a tested support matrix before extending those claims;
do not import a new authentication stack merely to make a gate appear closed.

**Formatter policy boundary is directionally sound.** The actual single/multi
paths gate requests before payload/redirect and again before Post; model telemetry
is removed from the payload type, avoiding a global preference-toggle race. Keep
this centralized boundary and explicit process consent. Consent governs admission
to dispatch; revocation rejects later admissions and stale UI application, but
cannot guarantee removal of bytes from an already admitted/in-flight request.
Do not hold a UI-shared lock across blocking network I/O to pretend otherwise.

**Async responses require identity plus content revision.** This is the reason for
BUG-002 and an invariant for later editor/project work. Inherited expression
formatting must also consider the selected DAX property/model/object context,
not only string equality. Inspect this in the same correction batch; any related
test finding stays in that batch rather than a new feature sprint.

**Retained-result budgets remain estimates.** Schema/row/cell checks and binary
copies are useful application limits. They do not bound provider allocations,
server work or exact managed heap size. Preserve intact cells/rows and explicit
incomplete reasons; fix numeric representation without rewriting result values.

**Single-file replacement remains a limited guarantee.** The CSV temporary-file
path preserves the destination on ordinary write failure; the document hash
check detects many stale writes. Neither provides a multi-file transaction,
universal concurrent-editor lock or power-loss recovery. R-004/OBS-001 remain as
recorded; no unsupported root cause or broad recovery redesign is assigned here.

## Visible application session

At the user's request, launched the verified repository build as **Tabular Editor
2.28.0** and loaded a synthetic model from
`projectmanagement/evidence/S001/lead/PbiBench-Demo.bim`. Ctrl+P opened Quick Open;
double-clicking Margin navigated to the model tree and displayed
`[Total Sales] - [Total Cost]`. The loaded-model title remained without a dirty
marker. This establishes a limited native model-open/Quick-Open navigation check.

The user then requested returning to code logic and leaving routine testing to
the light model. UI interaction stopped and the app was left open. No live query,
remote consent change, real business model edit, native Save/Save As journey or
real monitor-DPI transition was exercised. T10 is still incomplete.

## Gates and next action

| Gate | Decision / owner |
|---|---|
| BUG-001 / BUG-002 | Block offline acceptance; medium developer owns correction batch |
| Targeted corrected behavior and affected regression | Light tester after developer handoff; do not simply rerun the prior happy-path tests |
| In-process timeout architecture | Approved with explicit limitations; no transport/isolation rewrite |
| Canonical net10 / legacy references / remote CI | Open environment/verification gates; no new pass claim from this review |
| Native UI | Limited model-open/Quick-Open evidence added; remaining journey/DPI cases open |
| Desktop/XMLA/authentication | Still open; QA coordinates an approved disposable fixture |
| Whole S001 | Not accepted |
| Next feature sprint | Provisional DAX language/editor foundation, not authorized for implementation yet |

After corrected code and targeted QA, the lead may accept the explicitly narrower
offline milestone and schedule independent DAX authoring work while retaining
live/canonical gates with owners. This will be a recorded decision, not an automatic
acceptance triggered by green smoke tests.

No production code, production tests, package pins or framework targets were
changed in this review. Added isolated review evidence and management decisions;
no commit, push, merge or branch change. Keep lead time focused on logic and
architecture; light QA owns the next routine validation campaign.
