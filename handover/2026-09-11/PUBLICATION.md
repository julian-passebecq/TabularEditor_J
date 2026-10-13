# Publication and provenance

Destination: https://github.com/julian-passebecq/TabularEditor_J
Takeover branch: codex/pro-ai-handover-2026-09-11

Before this handover, origin/pbi-workflow-pro-v0.2 and the local stabilization branch ended at a7317e7ff1bbdb8949b7e6d82b6277b6e22300de (DAX Workbench/AMO adapter). Earlier v0.1/v0.2 foundations were already remote. `git fetch origin` succeeded; `git log --branches --not --remotes` found no unpublished local branch commits. The missing work was uncommitted source/tests/docs/evidence, not hidden commits.

This branch preserves that baseline plus all nonignored pending files from the main checkout, the management/evidence history, and six nonignored audit-checkout files copied into audit-checkout/. It is a takeover snapshot, not merged into master and not a release. Historical statements saying no commit/push describe the earlier sessions. The publication commit(s) on this branch supersede those statements.

## Who did what

- Owner Julian: product scope, model/role selection and request to consolidate/publish/stop the costly workflow.
- Earlier Codex implementation/audit work: v0.1/v0.2 foundation and stabilization; existing Git authors/history and audit documents preserve attribution. Exact model identities are not established by repository evidence.
- `Define AI sprint leadership workflow` (task 01a08247-2a91-7f31-ac2b-4cc71697bb02): technical lead; architecture/backlog/S001 packet, logic review, two reproduced defects and correction decision.
- `Execute active sprint passes` (task 01a08265-9f55-7d41-88ac-205dfc0f0372): developer role designated medium; implementation A/B/C, regression harness and correction batch.
- `Verify S001 and complete QA` (task 01a082c0-7ef4-77d0-bda1-b2bb00a50da9): independent tester role designated light; QA and correction QA/evidence/records. These tasks used the same main checkout, so their disk changes are included together.
- Current `Prepare AI handover branch`: repository/source audit, consolidation and handover/publication only; no product fixes or new sprint acceptance.

## Other checkouts and completeness

Git registers two worktrees: D:/PROJ/TabularEditor_J and D:/PROJ/TabularEditor_J-audit-v02. The latter is detached at a7317e7, has no tracked modifications and has six unpublished probe/log files. All six are preserved byte-for-byte under audit-checkout/; their originals remain untouched. They are historical baseline probes, not current test entry points. Their relative project references assumed the original checkout location and are intentionally not rewritten; use current PbiBench.Host.Smoke for current validation.

The app's available recent-task listing included the three related tasks above, all inactive/notLoaded; no additional active project worker was shown. Other projects have separate handover tasks and are outside this repository request. This is a checked local-worktree/recent-task inventory, not a claim to inspect inaccessible machines or all archived chat history. No source needs to be extracted from the inspected tasks' chats because their working directory is this checkout.

Ignored files are bin/obj outputs and restored packages (including evidence harness outputs); they remain local, reproducible and excluded from Git. Their relevant build logs and artifact hashes are included. No source files were found among the top-level ignored categories. The audit originals remain untracked in their old checkout, but are now archived in this branch; that is not missing work requiring a second push.

## Verification and limitations

Initial status/branches/worktrees and publication path inventory are in evidence/. Source identity check is described in README.md. A credential-pattern scan of pending evidence/docs/tests found only the intentional `Password=sentinel` privacy-test fixture. Existing logs contain local environment paths; they are historical evidence, not endpoint authorization.

This branch name does not match the existing PbiBench push-workflow filters (`codex/stabilize-pbibench-*`). Consequently publication itself does not establish a PbiBench CI result. Remote CI remains outstanding. No build/test result was invented for this handover, and existing test failures/environment gates are preserved.
