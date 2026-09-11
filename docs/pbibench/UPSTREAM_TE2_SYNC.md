# Upstream Tabular Editor 2 sync policy

## Upstream

Repository: `TabularEditor/TabularEditor`

Current pinned baseline for PbiBench foundation:
`7029129aa3f45d35f987d8f6ac7e5a971f28771c`

At branch creation, `julian-passebecq/TabularEditor_J:master` matched that exact upstream SHA.

## Rule

PbiBench must remain easy to rebase/sync when TE2 receives compatibility or critical fixes.

### Prefer

1. additive PbiBench projects/folders;
2. adapters and composition roots;
3. tiny shell hooks;
4. explicit project references;
5. generated/owned UI surfaces instead of large edits to upstream WinForms Designer files.

### Avoid

- mass-renaming TabularEditor/TOMWrapper namespaces;
- rewriting upstream project layout;
- moving upstream files merely for aesthetics;
- large FormMain.Designer.cs diffs;
- copying Fabric/MCP runtime dependencies into the TE2 executable;
- package upgrades unrelated to a PbiBench feature.

## Upstream-change ledger

Every PbiBench pass that edits an upstream-owned file must append to this table.

| PbiBench pass | Upstream file | Reason | Expected conflict risk |
|---|---|---|---|
| v0.1 foundation | none | additive core/docs only | none |

## Recommended sync procedure

```text
1. fetch upstream master
2. record old upstream pin
3. compare old pin -> new upstream pin
4. update fork master with upstream only
5. rebase/merge PbiBench branch onto new master
6. inspect the upstream-change ledger first
7. run upstream TE2 tests/build
8. run PbiBench core/host tests
9. update this document with the new pin
```

Do not resolve conflicts by blindly keeping the PbiBench side. TE2 behavior is the semantic foundation; PbiBench hooks should adapt around it.

## Provenance

TE2 remains MIT licensed under the repository root `LICENSE`. New PbiBench-owned code should preserve clear provenance and must not copy Tabular Editor 3 proprietary code/assets/internal implementation.

## S001 upstream formatter changes

| PbiBench pass | Upstream file | Reason | Expected conflict risk |
|---|---|---|---|
| S001-C | TabularEditor/UIServices/DaxFormatter.cs | Shared session/admin gate before payload/redirect; remove model-telemetry collection and inherited telemetry fields from request DTO; recording transport seam; explicit HTTP timeouts. Single/batch protocol options retained. | Medium: reconcile future SQLBI protocol/transport/telemetry changes against the shared gate. |
| S001-C | TabularEditor/Scripting/ScriptHelper.cs | Gate obsolete single, queued-object and enumerable/batch calls before throttling, dialogs or formatting. Disabled script/CLI requests fail clearly. | Low: three policy checks; retain UDF wrapper/comment extraction. |
| S001-C | TabularEditor/FormMain.cs | Inherited expression formatting runs off UI thread; gates consent, tracks edits/object identity/consent and prevents overlapping requests; safe failure status directs to Workbench consent. | Medium: expression editor event types and undo behavior are upstream-owned. |

Regression evidence: PbiBench.Host.Smoke/FormatterExportSmoke.cs executes the
actual single/multi proxy with recording redirect/post transport; actual expression
handler; script single/batch/UDF wrappers; fresh CLI single/batch denial; payload
field inventory; admin/revoke controls; stale/failed/cancelled responses and
Workbench undo. No automated remote SQLBI calls. See the S001 developer handoff.
No dependency or target-framework changes were made.

## S001 correction batch 01

| PbiBench pass | Upstream file | Reason | Expected conflict risk |
|---|---|---|---|
| S001-REWORK-01 | TabularEditor/UI/UIController.cs | Increment expression context generation at the existing handler assignment boundary, including unload/reload transitions. | Low: preserve the Handler property contract while retaining its private setter. |
| S001-REWORK-01 | TabularEditor/UI/UIController_ExpressionEditor.cs | Read-only context generation advances on current-object assignment and the existing DAX-property selector event, independent of equal text. | Low/medium: preserve generation invalidation when upstream changes expression selection. |
| S001-REWORK-01 | TabularEditor/FormMain.cs | Capture controller/context generation and reject delayed formatting after semantic-context transitions, including away/back. | Low: an additional guard in the existing async formatter. |

Rework01Smoke exercises the real model-bound UI controller, selector, expression
handler and controlled proxy with equal-text transitions. Same-context formatting
still applies; stale transitions do not. No query transport, model semantics,
package pins or target frameworks changed. Independent T14/T15 QA remains required.
