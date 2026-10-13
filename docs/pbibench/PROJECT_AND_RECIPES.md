# Project inventory and restricted recipes

Tools > PbiBench > Project opens a read-only context window. Choose a folder or
PBIP, refresh, and explicitly select among multiple candidates. Discovery is
inventory, not proof that every sibling report/model belongs to the selected
PBIP. Loaded, Live, Disk, Git and Baseline are shown separately. Model names are
labels rather than persistent model IDs. Baseline is not captured. Refresh is
manual; no filesystem watcher or synchronization is implemented.

Discovery preserves roots, scans at most 512 directories through depth 4, bounds
each directory enumeration to 512 matching entries, skips reparse directories
and common build/tool folders, and reports incomplete/access/limit conditions.
No recursive TMDL fallback bypasses those bounds. Candidate auto-selection is
allowed only for one candidate in complete discovery, or an explicit PBIP path.
Filesystem calls can block on unavailable network shares despite cooperative
cancellation. UNC and access-denied UI journeys remain Light environment checks.

Git status runs the installed Git executable without shell commands, optional
index locks or fsmonitor hooks. Working-directory paths are not interpolated into
arguments. Inherited repository-selection environment overrides are removed.
Output is bounded to 1 Mi characters per stream, 4,096 displayed entries and a
10-second cancellation request. NUL porcelain preserves rename pairs and unusual
filenames; the view escapes control characters. Export includes only the explicit
context contract (paths, capabilities, Git summary), never model rows, DAX or
connection strings. Status failures remain Unknown, never Clean.

Tools > PbiBench > Reviewed metadata recipes is a separate restricted lane from
TE2's trusted arbitrary C#. Only setting a display folder or hidden state for a
selected table's measures is supported. Preview displays every changed property;
Apply uses that exact immutable plan. Limits: 10,000 snapshot measures, 1,000
targets, 1,024-character requested values and 4 Mi characters of source metadata.
No C# is compiled, no file is modified, and no server/model save is performed.

The fingerprint covers all measure DAX identifiers, display folders and hidden
flags, not expressions or unrelated metadata. Apply also checks target object
identity, current handler, active batch and undo availability. TE2 owns setters,
one undo batch and attempted rollback for a property failure. Rollback/EndUpdate
failure is not claimed atomic; inspect model and undo state before saving after
an apply failure. No recovery file or crash-durability claim is made.

PROJ-001 and read-only portions of PROJ-002 are implemented for Light QA. PROJ-003/004,
VS Code handoff, automatic conflict detection and reviewed synchronization remain
deferred. AUTO-002 has this narrow recipe slice; AUTO-001 script documents and
AUTO-003 gallery remain deferred. Recipe source never pretends to sandbox C#.
