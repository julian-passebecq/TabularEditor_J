# TE2 Enhanced: getting started (portable build)

TE2 Enhanced is Tabular Editor 2 plus Power BI engineering tools, which are all under **Tools > PbiBench**.

This build is portable and unsigned. It is a qualification build, not a release. It installs nothing and changes no system setting.

## Install
1. You need Windows 10/11 and the .NET Framework 4.8.
2. The optional MCP companion needs any .NET 8 or later runtime.
3. Extract `TE2Enhanced-<sha8>-portable.zip` into any folder you own, for example `%USERPROFILE%\Tools\TE2Enhanced`.
4. Check the zip against `SHA256SUMS.txt`, or check single files against `MANIFEST.txt`.
5. Run `TabularEditor.exe`.

To uninstall, delete the folder. Per-user data lives in `%LOCALAPPDATA%\TE2Enhanced\`, and you can delete it whenever you like. It holds:

| Folder | Content |
|---|---|
| `recovery` | DAX drafts, opt-in |
| `journal` | reviewed file-write journals |
| `mcp` | published snapshot |

## First journey (about 10 minutes)
1. **Model.** Open a `.bim`, a TMDL folder or a PBIP semantic model with *File > Open*. To work against a live model, connect to Power BI Desktop or an XMLA endpoint.
2. **DAX.** In *Tools > PbiBench > DAX Workbench*:
   - open several tabs;
   - `Ctrl+Space` gives completion and `Ctrl+Shift+Space` gives signatures;
   - `F8` runs advisory checks;
   - `F5` runs the query, but only with a live connection.

   *Semantic View* shows where each object is used and which references are unresolved.
3. **Save and reopen.** Save the model. Then edit a `.tmdl` file outside TE2: the Project window (*Tools > PbiBench > Project*) shows a banner. TE2's own *Save* does not detect that conflict, so check *Review save target…* before you save.
4. **Report.** Open the PBIR report folder of a **copy** of a PBIP in *Tools > PbiBench > Report*. From there you can:
   - index the report and follow lineage to model fields;
   - apply a theme through the reviewed plan, which shows a diff and rejects files that changed meanwhile;
   - use *Restore journal* to go back byte for byte.
5. **Scripts and recipes.** C# scripts run only after you trust that exact text, and each run is one undo transaction. Recipes (rename with dependents, format strings) show a preview first and undo in one step.
6. **Checks.** Run curated BPA, assertions and profiling drafts. Profiling drafts open unsaved and never run before `F5`.
7. **MCP (read-only).** *Tools > PbiBench > Publish read-only snapshot* writes `%LOCALAPPDATA%\TE2Enhanced\mcp\snapshot-v1.json`. Expressions are left out unless you opt in. Then register the companion in an MCP client:
   ```json
   { "mcpServers": { "te2-enhanced": { "command": "C:\\path\\to\\TE2Enhanced\\mcp\\TE2Enhanced.Mcp.exe" } } }
   ```
   The companion answers only from that snapshot and never writes or connects to a model. `TE2Enhanced.Mcp.exe --self-test` checks it offline.

## Recovery and rollback
- **Model edits:** TE2 undo. For files, TE2 writes the model only when you *Save*.
- **DAX drafts:** opt in to recovery. After a crash, drafts come back as unsaved tabs. *Don't save* deletes only your own copy.
- **Reviewed file writes** (themes, file plans): each one is journaled. A failed write rolls back, and *Restore journal* undoes a completed one.
- **A save that fails with "Unable to remove the file to be replaced"** (OBS-001, rare, Windows file locking): the target is left intact, so retry the save. To allow one automatic retry, set `TE2ENHANCED_REPLACE_RETRY=1` before you start the program.

## Known limits
- Cancel and timeout are best effort. A long query bound by the formula engine keeps running on the server until it ends; the result is then reported as Cancelled.
- No live Power BI Desktop, remote XMLA or Entra authentication journey is certified yet.
- PBIR-Legacy and bookmarks are not indexed. Report-level measures are not covered. MCP has no write tools.

See `projectmanagement/features.csv` and `handover/2026-10-13/` in the source repository for the full status.
