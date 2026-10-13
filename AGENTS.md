# TE2 Enhanced - Pro implementation contract

## Product identity

This repository is **TE2 Enhanced**, the lighter Power BI engineering extension built directly on the Tabular Editor 2 fork in `julian-passebecq/TabularEditor_J`.

Do **not** confuse it with the older/full `powerbi_enhanced_dev` / standalone PbiBench product. Historical namespaces and folders in this repository may still use the internal name `PbiBench`; that does not authorize importing the old full application or expanding this fork into it.

## Current operating model

Use one strong **Pro implementation conversation** in the local `TE2` workspace. Pro owns architecture review, coding, refactoring, focused regression tests and implementation handoff.

A separate **Light model conversation will perform the broad independent Windows QA afterwards**. Therefore Pro should spend its budget primarily on implementation, not on repeating exhaustive manual test campaigns that Light can run later.

Pro must still keep the code buildable and run targeted automated/smoke checks for every subsystem it changes. Do not knowingly hand Light a broken tree.

Do not recreate the old lead/developer/tester multi-agent chain. Historical workflow documents are evidence only unless the user explicitly reactivates them.

## Authoritative starting point

Work from the existing takeover work on branch:

`codex/pro-ai-handover-2026-09-11`

Before changing anything, inspect the local workspace and record branch, HEAD, remotes, `git status --short --branch`, untracked/modified files, and toolchain versions. Preserve any local work newer than the remote branch. Do not reset, clean, discard, stash, rebase, merge, or switch branches blindly.

Read in this order:

1. `handover/2026-09-11/README.md`
2. `handover/2026-09-11/PRO_IMPLEMENTATION_MASTER_PROMPT.md`
3. `handover/2026-09-11/REMAINING.md`
4. `projectmanagement/STATUS.md`
5. `projectmanagement/ARCHITECTURE.md`
6. `projectmanagement/BACKLOG.md`
7. `projectmanagement/handoffs/S001-QA-REWORK-01.md`
8. `projectmanagement/reviews/S001-DECISION.md`
9. deeper implementation/test files only as needed

Newest takeover instructions override older workflow-routing language when they conflict.

## Do not restart completed work

S001 A/B/C and correction batch 01 already exist. BUG-001 and BUG-002 are QA-verified. Do not reimplement those fixes simply because the older lead decision says `REWORK_REQUIRED`.

Perform the focused code-logic review needed to close the outdated decision. Preserve honest open gates for live Desktop/XMLA/auth, canonical environments or native interaction where evidence is still absent.

## Implementation strategy

The goal is to advance the product as far as is technically coherent in one Pro run. Do not stop after writing a plan or after one tiny pass. Work through successive bounded workstreams, committing coherent checkpoints and continuing automatically until:

- the useful planned implementation that can be completed safely is exhausted;
- a genuine external/environment blocker prevents further implementation;
- a major product decision requires the user; or
- execution/context limits require a handoff.

Prioritize the roadmap and dependencies in `ARCHITECTURE.md`, `BACKLOG.md`, `REMAINING.md` and the master prompt. Prefer completed vertical slices over broad half-built scaffolding.

## Architectural boundaries

- Preserve the recognizable TE2 WinForms host and upstream editing behavior.
- TE2/TOMWrapper remain semantic/undo/serialization owners.
- `PbiBench.Core` remains neutral and must not import WinForms/TOM/AMO/HTTP/auth/provider-specific UI dependencies.
- Host/TOM/AMO/WinForms adapters remain under the TE2 integration area.
- Keep query execution on dedicated owned sessions; cancellation/timeout claims remain truthful and best-effort unless architecture changes are justified by evidence.
- Preserve explicit formatter consent, privacy boundaries and stale-response guards.
- Separate restricted reviewed recipes from arbitrary trusted C# execution.
- Treat Disk, Loaded, Live, Git and Baseline as distinct identities; no silent overwrite.
- Unknown report schemas remain read-only.
- New writes/bulk changes require proposal/review/stale detection plus undo or recovery.
- Do not copy proprietary TE3 code/assets or non-compatible third-party implementations.
- Keep Fabric administration external, deep timings/plans in DAX Studio, final report rendering in Power BI Desktop, and AI/agent orchestration outside the core product.

## Testing split

**Pro:** build after meaningful changes; add/run focused unit/integration/smoke tests around changed logic; verify the actual host launches when a change affects host wiring. Fix failures caused by Pro's work.

**Light later:** full independent clean rebuild/test campaign, real Windows UI journey, DPI/keyboard/focus, regression matrix, external-tool handoffs, live endpoints when approved, screenshots/logs, release GREEN/YELLOW/RED verdict.

Do not spend Pro's implementation budget recreating an exhaustive independent QA report that Light can produce later.

## Required Pro handoff

Before stopping, leave the branch buildable and create/update:

- `handover/2026-09-11/PRO_IMPLEMENTATION_STATUS.md` - completed work, commits, design decisions, known limits and remaining backlog;
- `handover/2026-09-11/LIGHT_QA_HANDOFF.md` - exact branch/HEAD, build commands, targeted changed areas, risk-based test matrix and any environment requirements.

Do not merge to `master` or claim a release. The Light QA pass and final release decision happen after Pro.
