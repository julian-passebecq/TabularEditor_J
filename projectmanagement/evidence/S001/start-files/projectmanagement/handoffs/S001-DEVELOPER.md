# S001 developer handoff

State: NOT_STARTED. This is a template, not an implementation report.
Owner: medium developer. Update during passes; complete before QA handoff.

## Short briefing

- User-visible outcome: pending.
- Passes completed / remaining: A, B, C pending.
- Recommended next actor/action: developer starts S001-A.
- Known baseline blockers: canonical net10 test needs supported SDK/environment;
  actual Desktop/XMLA and native interaction not yet exercised.

## Source and delivery identity

Record starting branch/HEAD/dirty snapshot; final branch/HEAD; commits or complete
dirty source manifest including untracked files; baseline-to-final comparison;
worktree path; built artifact identities. Explain pre-existing changes included
in acceptance. Do not provide only `git diff HEAD` if that hides committed work.

## Pass checkpoints

| Pass | State | Changed behavior/files | Checks and evidence | Remaining issue / exact next action |
|---|---|---|---|---|
| A | TODO | — | — | Establish baseline/tooling |
| B | TODO | — | — | Query lifecycle/diagnostics/bounds |
| C | TODO | — | — | Formatter/export/final delivery |

## Design notes for review

Explain query-session ownership and terminal-state races, cancellation/cleanup
order, connection/command timeout mechanism and limitations, retained-result
estimate, diagnostic allowlist, formatter gate/caller inventory and serialized
payload fields, stale formatting result behavior, and file replacement semantics.
Link exact code locations and any decision that differed from the sprint.

List each upstream-owned file changed, reason, conflict risk and ledger entry.
List dependencies/targets changed, or state none after checking the diff.

## Developer verification

| Test ID | Exact command/steps + source identity | Result | Evidence | Limits |
|---|---|---|---|---|
| Pending | — | NOT_RUN | — | — |

Separate actual passes, failures, blocked environment cases and unexecuted tests.
Do not copy baseline results as if they prove the final implementation.

## Risks, defects and escalation

For each issue: backlog ID, severity, reproduction, observed/expected, attempted
fixes, current owner, proposed next action and whether it blocks acceptance.
Include missing manual/live environment requirements as a single actionable list.

## Resume / handoff

If interrupted: last coherent checkpoint, modified files, checks pending, next
exact implementation step, unresolved reasoning and commands to resume.

At completion: set READY_FOR_QA in STATUS and here, summarize final scope, and tell
the user to start the light tester with the prompt from README. If scope is blocked,
state that precisely and link the lead escalation instead of claiming completion.
