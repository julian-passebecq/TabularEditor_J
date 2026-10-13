# PbiBench working agreement

Start with `projectmanagement/README.md` and `projectmanagement/STATUS.md`.
Follow the role assigned by the user and the active sprint packet. If the user asks
for development, testing, or a technical review without naming a sprint, use the
active sprint in STATUS. User instructions override this working agreement.

- Technical lead owns architecture, scope, sprint acceptance, and next-sprint decisions.
- Developer (medium model) implements the whole authorized sprint through its internal
  passes, writes focused regression checks, and prepares the developer handoff.
- Tester (light model) independently verifies behavior, reproduces defects, maintains
  backlog/branch/test records, and prepares the technical-review packet. Do not quietly
  fix production code while acting as the independent tester.
- Read `projectmanagement/WORKFLOW.md` for continuation, escalation, and handoff rules.
  Pass boundaries are checkpoints, not requests for another user prompt.
- Preserve existing uncommitted work. Record it before editing; do not reset, clean,
  switch branches, or stage everything indiscriminately.
- Keep neutral logic in `PbiBench.Core`; keep TOM/AMO/WinForms adapters under
  `TabularEditor/PbiBench`. Preserve upstream TE2 ownership and record necessary
  upstream edits in `docs/pbibench/UPSTREAM_TE2_SYNC.md`.
- Distinguish implemented, tested, blocked, and accepted. Test the built host after
  rebuilding it; compiling the smoke harness alone does not rebuild product code.
- Never claim live Power BI/XMLA validation from an offline fixture. Do not persist
  credentials, connection strings, business rows, or private DAX in management records.
- No agent may accept its own sprint on behalf of the technical lead. Production
  changes, test evidence, and unresolved issues must reach the lead for logic review.

This agreement does not automatically start other models or create scheduled work.
