# Automation V1: script workspace, reviewed recipes, recovery store

Supersedes the AUTO-001/AUTO-002 paragraphs at the end of `PROJECT_AND_RECIPES.md`.

## C# script workspace (AUTO-001)
Tools > PbiBench > *C# script workspace (trusted, not sandboxed)…*

- Up to 20 tabs; New / Open (.csx/.cs, 1 MiB text) / Save / Save as / Close with a dirty guard.
- Compile (F6) uses TE2's own `ScriptEngine.CompileScript` (same preprocessing, references and compiler
  preferences as Advanced Scripting). The Problems list shows errors first; double-click jumps to the line.
  Locations in class definitions hoisted by TE2 show as "(generated code)".
- Run (F5): refuses compile errors; asks for trust for the **exact** text (SHA-256 bound). Any edit revokes
  trust. The prompt states the script runs with the user's Windows permissions and is NOT sandboxed, and lists
  an advisory keyword scan that is explicitly not a security check.
- Execution: one TE2 undo batch (`BeginUpdate` → script → `EndUpdateAll`). On failure or cancel the batch is
  rolled back by default ("Roll back model changes if the script fails"). Effects outside the model (files,
  processes, network, DAX formatter calls made by the script) cannot be undone. No model save or deploy.
- Guards: compile result must match the document generation and hash; the loaded model must be the one the
  workspace was opened on; TE2 must not be inside another batch.

## Reviewed recipes (AUTO-002)
Tools > PbiBench > *Reviewed metadata recipes…* now uses the fixed `RecipeCatalog`:
display folder, hidden, format string, description, rename. Typed validators (length, control characters,
balanced format-string quotes, name whitespace). Descriptions are previewed in the LF form TE2 stores.
Format string is refused for measures with a format string expression.

Rename: exactly one measure; collision with another measure or a column of its table is refused; refused when
TE2 formula fix-up is off; refused when any DAX property mentions `[Name]` more often than TE2 tracks it
(strings, comments, KPIs, ambiguous column names). Otherwise the preview lists every dependent expression with
the text TE2 FormulaFixup will write (CR removed, qualified references become `[New]`). Apply verifies that TE2
produced exactly the previewed text and rolls the batch back otherwise. Report visuals and external tools are
not rewritten.

## RecoveryStore (core, generic)
`PbiBench.Core.Documents.RecoveryStore`: opt-in (disabled store never touches disk), disclosed folder
`%LOCALAPPDATA%\TE2Enhanced\recovery`, one content + one hash-manifest file per snapshot version, new files
only (temp write then move to a fresh name, old versions deleted afterwards; no `File.Replace`), SHA-256
verified on read, retention by days then count, explicit delete of store-owned files only. The script
workspace opt-in is a marker file `%LOCALAPPDATA%\TE2Enhanced\recovery.optin`; copies are written 3 s after
the last edit and on close, deleted on save or discard. Restored scripts start untrusted.
