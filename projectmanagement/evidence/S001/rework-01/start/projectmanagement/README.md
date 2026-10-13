# PbiBench project management

This folder is the working source of truth for delivery: what we are building, why,
who does the work, what must be tested, and when the technical lead returns.

**Current action:** S001 requires [correction batch 01](sprints/S001-REWORK-01.md)
after [technical-lead review](reviews/S001-DECISION.md). The medium developer fixes
the two correctness issues and related context guard, then hands off once to light
QA. STATUS remains authoritative; do not restart the completed A/B/C passes.

The user is the product owner. The technical lead owns architecture and code-logic
review. The medium model develops. The light model tests and maintains delivery
records. These are roles; the user chooses the actual model when starting a task.

## Start here

1. Read [STATUS](STATUS.md) for the active sprint, branch, and next action.
2. Read [WORKFLOW](WORKFLOW.md) for your role and continuation instructions.
3. Read the [active sprint](sprints/SPRINT-001.md) and [test plan](TEST-PLAN.md).
4. Consult [ARCHITECTURE](ARCHITECTURE.md) for boundaries and design decisions.
5. Update only the records assigned to your role.

| Record | Purpose | Main owner |
|---|---|---|
| [Architecture and product vision](ARCHITECTURE.md) | Final direction, responsibilities, invariants, reasons | Lead |
| [Feature backlog](BACKLOG.md) | Priorities, dependencies, acceptance outcomes | Lead prioritizes; tester maintains evidence/status |
| [Sprint 001](sprints/SPRINT-001.md) | Authorized implementation scope and internal milestones | Lead; developer records progress |
| [Test plan](TEST-PLAN.md) | Required automated, manual, and integration checks | Lead defines; tester executes/extends |
| [Branch and test register](REGISTERS.md) | Branch roles, test environments, unresolved validation | Tester |
| [Initial technical review](reviews/2026-09-08-baseline.md) | Source findings and fresh validation | Lead |
| [Developer handoff](handoffs/S001-DEVELOPER.md) | What changed, why, checks, remaining risks | Developer |
| [QA report](handoffs/S001-QA.md) | Independent evidence and review packet | Tester |
| [Sprint decision](reviews/S001-DECISION.md) | Lead's eventual acceptance or rework decision | Lead |

## One prompt per role handoff

**Start the medium developer:**

> Act as the developer. Read AGENTS.md and projectmanagement/README.md, then execute
> every ready pass of the active sprint in STATUS.md. Continue through the internal
> milestones without asking me for a new pass. Follow WORKFLOW.md, run your focused
> checks, and finish the developer handoff. Tell me clearly when the light tester
> should take over or when the technical lead is needed.

**Start the light tester:**

> Act as the independent tester and delivery-record maintainer. Read AGENTS.md,
> projectmanagement/STATUS.md, the active sprint, TEST-PLAN.md, and its developer
> handoff. Verify the exact delivered source, run the required checks, reproduce
> failures, maintain BACKLOG.md and REGISTERS.md, and complete the QA report. Tell me
> whether to return to the developer, call the technical lead, or provide a missing
> test environment. Do not silently fix production code or approve the sprint.

**Bring back the technical lead:**

> Act as technical lead. Read projectmanagement/STATUS.md and the current developer
> and QA handoffs. Audit the actual implementation and code logic, including failure
> paths and the complete sprint diff. Decide acceptance or rework in the sprint
> decision record, update architecture/backlog, and prepare the next substantial
> sprint if the gate is satisfied. Delegate routine evidence gathering only when
> useful; retain the architectural and logic decisions.

These prompts authorize substantial work within the active sprint; they do not
require the user to supervise each internal pass. If an app/runtime limit ends a
run, the agent leaves an exact resume point. A continuation resumes that point
instead of starting planning again. This documentation does not make idle models
run automatically.

## Relationship to existing documentation

`README_PBIBENCH.md` and `docs/pbibench/ARCHITECTURE.md` explain the product and upstream
boundaries. Older roadmaps and audits remain historical evidence. This folder owns
current sprint decisions and acceptance status. In particular,
`docs/pbibench/V0_2_MODEL_DAX.md` predates the query adapter, and unchecked items in
the older roadmap are not a reliable inventory of today's working tree. Update
those descriptions as part of Sprint 001; do not erase historical audit findings.
