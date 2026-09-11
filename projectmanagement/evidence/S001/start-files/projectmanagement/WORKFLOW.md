# Delivery workflow and role contracts

## Working rhythm

A sprint is a coherent outcome with several substantial development passes, one
independent QA campaign, and one technical-lead review. Passes are checkpoints
inside the authorized sprint. They are not separate permission gates.

The developer should complete A -> B -> C in the same active run when possible.
Several hours of useful work is appropriate; elapsed hours are not a deliverable
or a promise that the runtime will stay active. Do not pad work to fill time, rush
past failures, or end after one small function when ready work remains. Write a
short checkpoint after each cohesive subsystem, then continue. Save context before
limits are reached so another turn can resume without losing reasoning.

The normal user involvement is developer start, tester handoff, then lead review.
Fixable QA findings return to the developer as one consolidated rework batch,
followed by targeted QA and affected regression checks. The tester need not ask
the lead about every ordinary implementation bug.

## Responsibilities

| Role | Does | Does not do |
|---|---|---|
| Lead | Product decomposition, architecture, invariants, pass design, test strategy, difficult diagnosis, full sprint logic audit, acceptance and next sprint | Spend the whole review rerunning routine commands already backed by trustworthy evidence |
| Medium developer | Inspect current code; implement approved scope; add behavior-focused regression checks; build and debug; document decisions and risks | Expand into future epics, approve its own work, delegate all testing to QA, or stop for each internal milestone |
| Light tester | Rebuild/verify delivered source; run independent/adversarial scenarios; reproduce bugs; audit evidence and routine source paths; maintain branch/test/backlog records; summarize risks | Invent architecture, weaken expected results, mark blocked tests passed, silently fix production code, or accept a sprint |
| User | Product priorities, actual model selection, unavailable environment/access, material scope decisions | Supervise each internal coding step |

The tester may add test fixtures and tests when expected behavior is already
specified, and fix its own harness errors. Record those changes separately. A new
test exposing a product defect goes to the developer. The lead may assign bounded
evidence-gathering work to the light role; delegating evidence does not delegate
the final judgment. Do not create additional user-owned tasks without instruction.

## Developer procedure

1. Read STATUS, active sprint, architecture decisions and relevant source/tests.
   Record HEAD, branch, upstream, dirty paths, and existing changes before editing.
2. Work on the current authorized branch. Preserve other work; use isolated
   checkouts only when necessary and record where the delivery actually lives.
3. Complete the pass's behavior and focused regression checks. Review the diff for
   unintended upstream edits, exception paths, lifetime errors, and scope creep.
4. Record checkpoint, evidence, decisions, and exact next step in developer
   handoff/STATUS. Continue to the next ready pass without asking the user.
5. Complete all executable required checks and document genuine environment
   blocks. List changed upstream files in the upstream-sync ledger.
6. Finish the developer handoff and set READY_FOR_QA. End with the precise tester
   prompt and any environment preparation the tester needs. Do not say merely
   "done" or "tests pass".

Local checkpoints should be coherent and attributable. If making commits as part
of the development workflow, stage only inspected intended paths and record the
baseline first. Do not combine unknown pre-existing work into a blind commit.
No automatic push, merge, release, branch deletion, or master update is part of
this management setup. Those actions require the user's applicable authorization.

## Tester procedure

1. Match the handoff to the actual source and binaries. A head SHA alone is
   insufficient for a dirty tree: record dirty paths and a source hash manifest
   or preserved patch identity. Include the originally untracked files.
2. Execute TEST-PLAN from freshly built product artifacts. Treat developer test
   results as guidance, not independent QA evidence. Inspect assertions and
   fixtures so a green test really checks the stated behavior.
3. Test boundary/failure cases and the user journey, not only happy paths. Follow
   negative paths through code when live environments are unavailable, clearly
   labeling this SOURCE_REVIEW rather than runtime evidence.
4. Record each result with test ID, source identity, environment, exact command or
   steps, exit code/observation, expected vs actual, evidence location, and limits.
5. Update the backlog and branch/test register. Keep all discovered issues,
   including deferred ones, linked to an ID, owner, severity, and next action.
6. Produce a short lead briefing plus detailed evidence links in S001-QA. Say
   READY_FOR_LEAD_REVIEW, RETURN_TO_DEV, or NEEDS_LEAD_DECISION. Missing live tests
   must remain visible even when all available automated tests pass.

## State and evidence vocabulary

Sprint: READY_FOR_DEV -> DEVELOPING -> READY_FOR_QA -> TESTING ->
READY_FOR_LEAD_REVIEW -> ACCEPTED. Use REWORK for a developer correction batch and
BLOCKED for a specific dependency, listing independent work that can continue.

Backlog: IDEA, PLANNED, READY, IN_PROGRESS, IMPLEMENTED, QA_VERIFIED, ACCEPTED,
DEFERRED, BLOCKED. Only the lead promotes to ACCEPTED or changes priority/scope.
The tester may add defects and maintain factual status; architectural proposals
go into the decision queue for the lead.

Tests: PASS, FAIL, BLOCKED_ENV, NOT_RUN, NOT_APPLICABLE. A historical report is
REPORTED_PRIOR, not a fresh PASS. NOT_APPLICABLE needs a reason. A filtered run
does not establish that excluded tests pass. A retry is recorded with its cause.

## Escalation without unnecessary interruption

Continue with local judgment for naming, private helper structure, tests, layout
adjustments, and ordinary fixes that preserve approved behavior. Batch related
questions into one written escalation and continue independent approved work.

Call the lead early for:

- A required change to architecture, a public contract's meaning, transport,
  target framework, material dependency, or the mutation/recovery model.
- A plausible data-loss, credential-disclosure, editing-session interference,
  or incorrect-results defect that the existing design does not resolve.
- Cancellation/cleanup behavior that cannot be made sound with the pinned AMO
  library, or ambiguity about what the endpoint actually guarantees.
- Two substantive attempted fixes that fail for the same root problem, with
  competing explanations. Do not spend an entire pass cycling variations blindly.
- Conflicting requirements, expansion outside the active sprint, or evidence
  that a previously accepted invariant was wrong.

The escalation packet states: expected behavior, observed behavior, minimal
reproduction, affected files, attempted fixes and evidence, proposed options and
tradeoffs, exact decision required, and independent work still possible.

Call the user only for information/access the agents cannot obtain or a product
decision beyond the lead's scope. When a real Desktop/XMLA environment is missing,
continue offline checks and list the exact remaining manual session as a batch.
Never pretend a wait or a blocked environment constitutes approval.

## Lead review and sprint sizing feedback

Read the short QA packet first, then the complete product diff and relevant
surrounding code; include pre-existing stabilization code being accepted. Follow
data and control flow across Core, adapter, UI, files, and upstream hooks. Audit
ordering, concurrency, cancellation, disposal, false success, stale writes,
privacy, error handling, compatibility, and whether tests could miss a defect.
Spot-check evidence and rerun targeted cases where the implementation or report
warrants it. Do not limit the review to the lines highlighted by QA.

Record ACCEPTED, REWORK_REQUIRED, or BLOCKED_ACCEPTANCE, with specific findings
and IDs. A missing live gate prevents a live-readiness claim; the lead may record
an explicitly narrower offline milestone and authorize independent next work
without declaring the entire sprint accepted. Every deferral has a reason, owner,
follow-up gate, and affected product claim.

After a successful gate, prioritize the backlog and write the next detailed
sprint. At close, record number of developer restarts/user prompts, approximate
active work per pass if known, rework cycles, and feedback about pass length.
Adjust the next packet to reduce unnecessary user intervention; do not optimize
for arbitrary tiny passes or unverifiable multi-day batches.
