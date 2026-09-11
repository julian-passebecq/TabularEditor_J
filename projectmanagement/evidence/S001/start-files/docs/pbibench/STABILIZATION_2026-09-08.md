# First stabilization slice — 8 September 2026

Branch: `codex/stabilize-pbibench-v02`, based on product head `a7317e7`.

The TE2 host now compiles, and the three PbiBench dialogs pass automated construction and layout checks on .NET Framework 4.8.

## Third slice — Local DAX documents

The Workbench now has a File menu with New (Ctrl+N), Open (Ctrl+O), Save (Ctrl+S) and Save As (Ctrl+Shift+S). Files can be edited in offline draft mode. An asterisk in the title tracks edits, including changes made by undo/redo; newline normalization by the text control does not itself mark a loaded file dirty.

Closing, creating a new document, opening another file and recalling history offer Save/Discard/Cancel when edits are unsaved. A cancelled Save As or failed save aborts the replacement. Failed Open retains the existing draft. Recalled history becomes an untitled unsaved document, preventing accidental reuse of the previous file's save destination. Opening or creating a document clears stale results.

The document service reads BOM-marked text and UTF-8 and writes UTF-8. Reads/writes are limited to 4 MiB. It detects changes/deletion of the last loaded or saved file and refuses an overwrite, offering Save As under another name through the error message. Saving writes a sibling temporary file before replacement; failure leaves the document marked unsaved and does not change its path. These checks do not constitute continuous file watching or a transactional lock against concurrent editors.

Document commands are disabled while a query runs. Closing during execution requests cancellation and leaves the editor open; close again after cancellation to save or discard the draft. No autosave, background persistence or crash recovery has been added.

Validation: Release build and Core smoke passed; host smoke covers the earlier layout/query checks plus Unicode/BOM round trips, newline normalization, atomic replacement, external edits/deletion, oversized/invalid files, failed Save As/Open, dirty titles, close/replacement cancellation, history isolation and closing during execution. Dialog choices are supplied by a test adapter, so automated checks do not display file dialogs. Native dialog interaction and real Desktop/XMLA execution remain manual acceptance work.

## Second slice — DAX execution and history

- Execute/F5/Ctrl+Enter runs selected text when a selection exists, otherwise the whole document. The button displays `Execute selection` to make the scope visible. Whitespace-only selections do not run anything.
- History now preserves Completed, Failed and Cancelled states. Completed entries show returned rows; `2+`, for example, indicates two returned rows with additional rows truncated. History snapshots this metadata without retaining the result row arrays. Existing boolean success callers remain supported.
- History loading and CSV export are disabled while execution is pending. Failed and cancelled runs clear earlier results, and the editor becomes editable again after completion.
- A provider-thrown cancellation following user cancellation is recorded as Cancelled. Unexpected provider exceptions show a generic message rather than their raw potentially sensitive details. This does not replace the AMO adapter's still-pending diagnostic-redaction work.
- Extended host smoke uses a controlled provider to exercise selection/document execution, whitespace selection, row/timeout propagation, truncated results, displayed history, duplicate-run prevention, cancellation and failure recovery. Core smoke verifies result metadata is copied and unfinished executions cannot enter history.

Validation: full Release host build, all dialog/layout smoke checks, the new Workbench execution checks, and Core smoke passed locally. Core smoke used the same net9.0 override described below. Controlled-provider tests do not establish live AMO timeout/cancellation correctness. No changes have been pushed.

## Changes

- Alias the DAX query adapter's `Server` to the tabular AMO type. This resolves CS0104 with the existing pinned Analysis Services package; no transport or dependency change was needed.
- Give DAX Workbench's two split containers and Semantic View's split container an initial size before applying splitter distances and panel minima. Docking only sizes these controls after parenting, so their previous default size caused construction to throw.
- Add `PbiBench.Host.Smoke`, which tests the built host assembly through reflection. It does not duplicate production form sources or change upstream type visibility. The fixture uses an offline model with two measures.
- Check construction, minimum size, enlargement, simulated 150% control scaling, minimum size after scaling, and disabled offline query execution. No window is displayed and no model is saved.
- Run the new host checks after the full host build in CI. Host/TOM/ANTLR changes now trigger that workflow, alongside PbiBench changes.

## Run the checks

After the normal legacy NuGet restore, Core restore, ANTLR Debug generation and Release host build:

```powershell
dotnet build PbiBench.Host.Smoke/PbiBench.Host.Smoke.csproj -c Release --nologo
& ./PbiBench.Host.Smoke/bin/Release/net48/PbiBench.Host.Smoke.exe
```

The smoke project deliberately references already-built Release artifacts. Rebuild the host after editing product source, then rebuild/run smoke. Building the smoke project alone does not rebuild TE2.

With .NET 10 installed, the existing Core check remains:

```powershell
dotnet run --project PbiBench.Core.Smoke/PbiBench.Core.Smoke.csproj -c Release
```

## Validation

| Check | Local result |
|---|---|
| Full TE2 Release host build | Passed; existing upstream warnings remain |
| Host smoke build | Passed without warnings |
| Quick Open | Construction, resize and scaled layout passed |
| Semantic View | Construction, resize and scaled layout passed |
| DAX Workbench | Construction, resize, scaled layout and disabled offline Execute passed |
| Existing Core smoke source | Passed using a command-line net9.0 override; the repository continues to target net10.0 |
| Legacy `TabularEditorTest` build | Blocked by unavailable legacy Visual Studio test-framework references |
| Remote CI | Updated but not executed; changes have not been pushed |
| Real Desktop/XMLA query lifecycle and real monitor DPI transitions | Not validated in this slice |

The development machine has SDK 9.0.101. Core checks were run by restoring/building the smoke project with a net9.0 override, restoring Core again at its normal netstandard2.0 target, and building smoke without project-reference rebuilds. No framework target was changed in the repository. The full host used the installed Visual Studio Build Tools with process-local SDK resolver settings, as described in the audit.

## Next acceptance work

Exercise the live query adapter on a known Desktop/XMLA test endpoint: success, invalid DAX, truncation, cancellation, timeout, authentication and preservation of the editing session. Then address the audit's remaining query-lifecycle and formatter-policy gaps before extending DAX authoring. The Workbench still uses its existing plain text editor and one-result-set contract.
