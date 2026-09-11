# S001 correction batch 01 — result fidelity and async document identity

State: READY_FOR_DEV within S001 REWORK. Owner: medium developer.
QA: light tester. Acceptance: technical lead.

Read [lead decision](../reviews/S001-DECISION.md) first. This packet supersedes
the completed A/B/C implementation checklist for the next development assignment.
Preserve the full dirty delivery and previous evidence. No new branch, shell
redesign, language-service port, query transport or authentication stack is needed.

## One cohesive developer assignment

Complete both corrections and the directly related context audit, then prepare
one handoff. Internal checkpoints are not user approval gates. This correction
batch can be shorter than a feature sprint; do not manufacture unrelated work to
fill hours or require the user to start a separate pass for each bug.

### 1. Preserve floating-point values at the conversion boundary — BUG-001

The shared `DaxResultRetention.FormatValue` is the owner for grid/CSV text
representation. Add explicit Double G17 and Single G9 formatting with invariant
culture, before generic Convert.ToString. Keep the current typed retained values;
do not convert them to strings during query materialization.

Preserve decimal/integer/bool/text/Unicode/null/binary/date/offset/span behavior.
Do not infer numeric types from strings or apply rounding for display to the CSV
data path. If future display formatting becomes more compact, it must be a distinct
presentation decision with the raw export preserved.

Finite values must round-trip to the same numeric value on net48. Explicitly
document non-finite values (NaN, positive/negative infinity) and signed-zero
representation; do not claim CSV is a bit-preserving binary serialization. Existing
invariant textual special values and canonical zero are acceptable if tested and
documented. No new CSV dialect or spreadsheet-formula transformation is in scope.

### 2. Track logical editing context separately from text — BUG-002

Introduce a monotonically increasing Workbench document generation in the host.
Advance it on every successful document replacement: New, Open (including reopening
the same path) and history recall. An equal text string must still advance the
generation. A failed/cancelled Open/New/replacement keeps the old generation and
document intact. Capture generation plus text revision when admitting formatting;
check both, consent revision, cancellation and disposal before applying a result.

The existing DaxDocument instance is reused, so reference equality alone does not
identify a new document. Paths also fail for untitled documents, reopening and
history. A generation is sufficient; no generic event bus or workspace framework.

Save/Save As are persistence actions on the same logical editing buffer, not
replacements in this design. They need not advance the generation solely because
the destination changes. If a pending format subsequently applies to that same
buffer, it must correctly become dirty again. Preserve existing undo semantics.

Audit the inherited async expression formatter in this same pass. Capture or
invalidate on its semantic editing context: model/handler, object and selected
DAX property, as well as content/consent. Do not rely on TextChanged happening when
two contexts contain identical text. Reuse TE2 context/selection events or a small
exposed read-only context token; keep upstream changes minimal and in the ledger.
Do not change model semantics or introduce a second expression model. Any concrete
related context bug found belongs in this correction batch with evidence.

### 3. Finish and hand off once

Add focused regression checks for these defects as part of development. Ensure
the production assemblies are rebuilt before running the corrected host tests.
The lead's isolated probe is reproduction evidence, not the sole final test suite.
Light QA owns the broader edge-case execution and records below.

Append a correction section to S001-DEVELOPER rather than erasing the original
delivery. Record design choice, files, checks, source and artifact identity, any
upstream ledger entries and open gates. Refresh final delivery hashes while
preserving the earlier manifests as historical evidence. Do not overwrite the
old QA report to make it look like it tested the new source.

Set STATUS to READY_FOR_QA and point to this batch and T14/T15. Tell the user to
start the light tester once for the consolidated correction delivery. No S002
feature implementation until a lead decision.

## Light tester assignment after the correction

Rebuild/identify the corrected source and independently run:

- **T14:** the exact reported Double/Single examples, representative adjacent
  representable values, small/subnormal/large finite values, positive/negative
  values, special values and signed zero policy under en-US and fr-CH. Verify the
  actual CSV decoded fields round-trip numerically; compare expected typed values,
  not just output generated by the same formatting helper. Check the grid uses
  the corrected shared representation. Preserve decimal/date/text behavior.
- **T15:** hold a recording formatter response for A; open equal-text B; release
  A's response. B's text, path, dirty state and undo history must stay unchanged.
  Repeat successful same-path reopen, New/history replacement and failed/cancelled
  replacement; check same-buffer Save/Save As behavior. Ensure real child controls
  are created, and include a native journey when that capability is available.
- **T15 expression context:** delayed response while switching object/model/DAX
  property, including equal-text contexts and away/back transitions; no application
  to a replaced context. Valid same-context formatting remains undoable/consistent
  with TE2 edit semantics. Inspect the actual context guard, not just mock text.
- Affected T07/T08/T09 and existing document/history/undo checks; the standard
  built-host smoke once on final source. Rerun the wider campaign only for relevant
  new changes or failures. Retain the net10/live/CI blocks honestly.

Produce `handoffs/S001-QA-REWORK-01.md` with exact source/evidence, both defect
outcomes, any related findings and remaining gates. Update BACKLOG/REGISTERS/STATUS.
If green, route to lead for a focused correction review and the next-sprint
decision. Do not mark whole S001 accepted.
