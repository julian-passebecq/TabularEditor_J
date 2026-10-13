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
| v0.2–S001 (reconciled 2026-10-13 from `git diff 7029129`) | `TabularEditor/FormMain.cs` | PbiBench menu/shortcut composition hooks; async expression formatting with explicit consent and stale-response guard (BUG-002) | medium: upstream edits to `actExpressionFormatDAX_Execute` conflict |
| v0.2–S001 | `TabularEditor/UI/UIController.cs` | `Handler` setter bumps `ExpressionEditorContextGeneration` | low |
| v0.2–S001 | `TabularEditor/UI/UIController_ExpressionEditor.cs` | context-generation counter for object/property transitions | low |
| v0.2–S001 | `TabularEditor/UIServices/DaxFormatter.cs` | formatter transport behind consent/policy service; no telemetry | medium: whole-file rewrite of the formatter client |
| v0.2–S001 | `TabularEditor/Scripting/ScriptHelper.cs` | script formatter entry points call `RemoteFormatterConsent.RequireEnabled()` instead of policy-only check | low |
| (all) | `Directory.Build.targets` (repo root, not upstream) | globs `TabularEditor/PbiBench/**/*.cs` and references PbiBench.Core so `TabularEditor.csproj` stays untouched | none |

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
# 2026-09-14 Pro implementation note

New authoring/results, relationship inventory, project view and recipe features
remain under `TabularEditor/PbiBench`, Core and smoke projects. No additional
upstream-owned TE2/TOMWrapper production files or package pins were changed in
this pass. TE2 lexer/TOM identifiers/navigation/setters/undo are reused directly.
The historical upstream edits below remain required and preserved.

# 2026-10-13 Light QA fixes (D1-D17)

All fixes stay under `TabularEditor/PbiBench`, `PbiBench.Core`, `TE2Enhanced.Mcp`, the smoke projects,
`Scripts/ci` and `.gitlab-ci.yml` (D12 tool pins only). No upstream-owned TE2/TOMWrapper production file,
`TabularEditor.csproj`, `PbiBenchShortcutFilter.cs` or package pin was changed. Dialog focus return uses
the existing `UIElements.TreeView`; keyboard handling is per PbiBench form (`ProcessCmdKey`).
