# TE2 Enhanced - Codex local audit contract

## Product identity

This repository is **TE2 Enhanced**, the lighter Power BI engineering extension built directly on the Tabular Editor 2 fork in `julian-passebecq/TabularEditor_J`.

Do **not** confuse this project with the older/full `powerbi_enhanced_dev` / standalone PbiBench product. Some historical namespaces, folders and management documents inside this repository still use the internal name `PbiBench`; those names do not change the repository/product identity for this audit.

## Operating model

Use **one Codex conversation in the local `TE2` workspace**. A multi-agent lead/developer/tester chain is not required for routine validation. Historical multi-role documents remain evidence only unless the user explicitly reactivates that workflow.

The current job is release-candidate **audit and local Windows validation first**, not a new feature pass.

## Read first

1. `handover/2026-09-11/README.md`
2. `projectmanagement/STATUS.md`
3. `projectmanagement/handoffs/S001-QA-REWORK-01.md`
4. `projectmanagement/reviews/S001-DECISION.md`
5. relevant verification scripts/tests referenced by those files

Treat the newest handover/status entries as authoritative when older documents conflict.

## Repository safety

Before any build or edit, record:

- repository root
- `git status --short --branch`
- current branch and HEAD
- remotes
- tracked/untracked changes
- available .NET SDK/MSBuild/NuGet versions

Preserve all existing work. Do **not** reset, clean, discard, stash, switch branches, rebase, merge, or stage everything automatically. Do not move work to the old PbiBench repository.

The handover branch `codex/pro-ai-handover-2026-09-11` is the intended review branch when that is what the local TE2 workspace already has checked out. Do not switch to it blindly if the local workspace contains different/newer work; inspect first and report any divergence.

## Audit-first rule

Do not begin another implementation pass merely because an older sprint document contains TODOs.

First determine the actual current state and answer:

1. Does the current source build on this Windows laptop?
2. Do the canonical/focused tests pass?
3. Does `Scripts/Verify-PbiBench.ps1` pass, or exactly which gate is blocked and why?
4. Does the **actual rebuilt Tabular Editor host** launch and behave correctly?
5. Are BUG-001 and BUG-002 still fixed in the current source?
6. Which previously open gates are now testable on this laptop?
7. Is there any reproducible release-blocking defect that needs a Pro coding pass?

Do not silently alter production code while performing this audit. Test-only diagnostics may be created outside production paths or clearly marked as audit evidence.

## Required local Windows validation

Use the real locally rebuilt TE2 Enhanced application, not only smoke-harness compilation.

At minimum verify, where the environment allows:

- clean restore/build of the intended solution/project path;
- canonical and focused automated tests referenced by the current handover;
- verification runner and both smoke lanes;
- launch of the actual `TabularEditor.exe`/enhanced host built from current source;
- opening the supplied/synthetic BIM test model;
- model tree/property/editor basics inherited from TE2;
- Ctrl+P / quick-open journey and navigation to the known `Margin` fixture when available;
- DAX document open/edit/save behavior and external-change protection;
- DAX query lifecycle states, cancellation/timeout/result limits using safe fixtures;
- CSV export precision/encoding behavior, including the former float/double regression;
- remote DAX formatting consent plus stale-response/document-replacement protection, without sending private model data;
- keyboard/focus/resize/DPI sanity for the enhanced UI;
- no regression to ordinary TE2 workflows caused by the enhancement layer.

For live Power BI Desktop/XMLA/authenticated endpoints: test only when the user explicitly supplies/approves an endpoint and credentials. Otherwise report the gate as `BLOCKED_ENV`; never convert offline fixtures into a live-readiness claim.

## Evidence

Create a concise local audit folder, preferably under `artifacts/local-audit-<timestamp>/`, containing only non-sensitive evidence such as:

- environment/version summary
- commands run and exit codes
- test/build logs or summaries
- screenshots of the real application for the required UI journeys
- reproduced defect notes
- final audit report

Do not store credentials, connection strings, access tokens, customer data, private DAX, or business rows.

## Decision at the end

Return exactly one release recommendation:

- `GREEN - no Pro pass needed`: current candidate is technically sound; only explicitly listed environment/live gates remain.
- `YELLOW - bounded validation/fix`: a small, well-reproduced issue or environment gate remains; describe the smallest next action.
- `RED - Pro pass needed`: one or more reproducible product/architecture defects require substantive coding. Provide exact reproduction steps, failing evidence, suspected ownership/files, and acceptance criteria for the Pro model.

Do not start a broad redesign. Do not expand this lighter TE2 fork into the old full PbiBench product.
