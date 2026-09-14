# Pro implementation status

2026-09-14. Product: TE2 Enhanced. Implementation pass; independent Light QA follows.

## Preserved starting state

- Branch `codex/pro-ai-handover-2026-09-11`, HEAD `24d1f26` at intake.
- Tracked and untracked working tree clean. Origin `https://github.com/julian-passebecq/TabularEditor_J.git`; upstream `https://github.com/TabularEditor/TabularEditor.git`.
- Fetched origin; inspected three documentation-only commits; fast-forwarded to `9fc048e`. No reset, clean, stash, rebase or discarded local work.
- Windows; installed SDK 9.0.101 and 9 preview; VS Build Tools 17.12; net48 reference assemblies. Installed private SDK 10.0.401 in ignored `.tools/dotnet`; system SDK unchanged. NuGet executable reused as a tool only from the historical path, no source imported from the old application.

## Current checkpoint

S001 correction code reviewed against independent correction QA. Explicit G17/G9 precedes generic formatting; zero policy is intentional. Workbench generation changes on successful document replacement and is captured/checked with text and consent revisions. TE2 handler/object/property transitions invalidate inherited expression responses, including away/back transitions. No reimplementation of BUG-001/002 needed.

The master prompt explicitly delegates focused historical acceptance to Pro. The evidenced S001 offline correction milestone is accepted; this does not accept new Pro work, live readiness or a release. Native/DPI, live Desktop/XMLA/auth, legacy regression references and remote CI remain open. OBS-001 stays open pending reproduction.

Foundation work in progress: allow canonical CLI SDK independently of VS legacy SDK; add takeover branch CI trigger and preserve logs. First build rebuilt actual host successfully; canonical CLI inherited legacy SDK path, so the runner now isolates that environment per CLI stage. Retrying with separate evidence directory.

Remaining dependency order: local authoring service; multiple result sets/documents; model usage; trusted scripts/reviewed recipes; bounded project discovery/context; report index; Explore; Optimize; integration. No broad feature or release completion claim.

Foundation verification: all seven stages PASS, exit 0, logs in .tools/verification-foundation. Canonical net10 now passes locally. Legacy TE2 tests remain isolated from this runner: missing legacy VisualStudio.QualityTools reference is not retargeted or silently counted as passing. Remote CI remains NOT_RUN.

## DAX and Model checkpoint

DAX-010/011 partial: local insertion picker from pinned TE2 function vocabulary and fresh TOM model objects, exact identifier quoting, navigation, nested parameter help and advisory delimiter checks. See docs/pbibench/DAX_AUTHORING.md for explicit limits. No remote authoring traffic or replacement of expression editing.

DAX-012 implemented for controlled-reader verification: zero/multiple sets, 32 provider-set cap, shared 32 MiB retained estimate, per-set row/cell/column limits, selector/copy/export for the chosen set. First-set Columns/Rows compatibility retained; history counts rows across sets. Cancellation still owns the dedicated session through cleanup. NextResult can block/drain provider work: application bounds do not bound server work. Memory/set-cap stops explicitly disclose remaining results not read. Live AMO multiple-rowset support remains an independent endpoint gate.

MODEL-002 partial: Semantic View adds a read-only all-table-relationships inventory with cardinality, filtering, active state and unresolved endpoints. Navigation stays with TE2. General unresolved DAX binding and report usage remain deferred.

Actual host rebuild, canonical Core and complete host smoke PASS in .tools/verification-model, including new authoring corpus/rename/delete, unresolved relationship and multiple-result cases. The first authoring test ran after a historical fixture replaced/disposed the singleton handler; moved that new test before those fixtures, without changing product behavior. New product changes have not been independently accepted.
