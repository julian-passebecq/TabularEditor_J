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

## Project and recipe checkpoint

PROJ-001: bounded root-preserving discovery, explicit multiple-PBIP selection, no unbounded TMDL recursion, reparse/access/limit diagnostics and cancellation. PROJ-002 partial: read-only Project window, manual refresh, separate Disk/Loaded/Live/Git/Baseline labels, bounded real Git porcelain status including rename/untracked state, folder open and explicit credential-free context export. Folder inventory does not infer PBIP associations. No reviewed file synchronization yet.

AUTO-002 partial: reviewed folder/hidden metadata changes for a selected table's measures. Immutable exact diff, bounded allowlist, source fingerprint and object/context checks, one TE2 undo batch, attempted rollback on rejected setter. Arbitrary C# remains a separate trusted upstream lane; no sandbox claim. Tests prove preview purity, stale rejection, forbidden operations, single undo and rollback of the first edit when a second setter throws.

Checks: .tools/verification-recipes all seven build/Core/host stages PASS. Follow-up host harness rebuild and full host smoke with additional rollback test PASS (.tools/verification-recipes/host-rollback.log). Project CLI smoke initializes only a synthetic temp repository and proves actual Git untracked state. Project form was shown/closed in the built-host harness.

OBS-001 recurred once in .tools/verification-project/host-smoke-run.log: IOException at File.Replace, 'Unable to remove the file to be replaced', second synthetic Save. Unchanged-binary retry passed; later rebuilt-harness and recipe runs passed. Root cause remains unknown. No retry was hidden inside product save, no test weakened, and the first failure remains preserved. Light must investigate file locking/permissions and failed-save state.

## Explore and final verification

EXP-001 partial: typed equality table-preview builder (text/decimal/Boolean/blank), exact identifier escaping, explicit sort, 1–5,000 retained rows, opens unsaved DAX draft without automatic execution. Model/table/column identity/name guard rejects stale preview execution. TOPN ties can return more server rows; application retention still caps rows and bytes. No server-cost or stable-unsorted-order claim. Profiling/distributions/Pivot Lab are not implemented.

Final complete runner: .tools/verification-delivery, exit 0, all seven stages PASS after the final production edits. Canonical Core includes discovery/Git parsing and adversarial typed-preview tests. Built-host smoke includes local authoring, metadata freshness, relationships, zero/many results, per-set rows, shared budgets, cancellation between sets, stale preview, real synthetic Git, recipes/undo/rollback and inherited S001 formatter/export/document regressions. New Project, recipe and preview forms were shown/closed by the harness. Standalone rebuilt executable also started as an owned hidden process and was responsive; no native model-open/menu/DPI journey is claimed from this launch. That owned launch was cleaned up; existing user processes were untouched.

Compact logs, binary hashes and both OBS-001 failures are committed under evidence/pro-2026-09-14. Full intermediate build logs remain local under ignored .tools. OBS-001 reproduced on both document and CSV File.Replace; the final run passed but the observation is NOT closed. No product retry or sleep workaround was introduced. Context-export temporary cleanup was extended to cover write failure as well as replacement failure.

## Scope remaining, in dependency order

1. Independent Light Windows QA for delivered slices, especially OBS-001, native UI and undo/failure paths. Legacy test-reference provisioning, remote clean-checkout CI and approved live/auth/multiple-rowset evidence remain open.
2. DAX-010/011 full inline/shared expression authoring, comprehensive signatures and diagnostics. DAX-013 multi-documents/conflict/recovery and DAX-014 DAX Studio handoff remain unimplemented. Single-document behavior is retained.
3. MODEL-001 large-model/native robustness and MODEL-002 unresolved DAX/report usage beyond table-relationship inventory.
4. AUTO-001 script documents/diagnostics/trusted-execution workspace; broader AUTO-002 allowlisted recipes only after QA; AUTO-003 curated gallery.
5. PROJ-002 persistent identity/comparison and VS Code handoff; PROJ-003 external-change tracking; PROJ-004 reviewed synchronization/recovery. Current inventory refresh is manual and read-only.
6. REPORT-001 schema-aware PBIR index/lineage, then REPORT-002/003 reviewed report/theme changes and restore. No report mutation or new schema claim in this pass.
7. EXP-001 live preview QA and richer typed operators; EXP-002 profiling/distributions/Pivot Lab.
8. OPT-001/002 curated BPA/assertions and bounded storage/usage evidence; existing upstream BPA remains unchanged.
9. Cross-feature integration and release-candidate qualification after QA. No release, master merge or push performed.

This is a substantial implementation checkpoint at the end of the Pro run, not completion of the whole master roadmap. Highest regression risks: result traversal/selection, recipe partial-failure/undo, filesystem/Git bounds, modal navigation and Windows replacement behavior. New work is READY_FOR_LIGHT_QA, not self-accepted.
