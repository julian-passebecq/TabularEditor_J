# TE2 Enhanced MCP companion — interface v1 (study + read-only prototype)

Status 2026-10-13: **prototype, read-only, self-tested offline.** Not a dependency of the TE2 host, of Fabric Studio or of anything else. No live MCP client certification yet.

## Why a separate companion
- TE2/TOMWrapper own semantic editing, undo and serialization. An agent should not get a second write path that bypasses TE2's undo, stale checks and review UI.
- Fabric Studio owns Fabric/project UX. It may consume this interface later, but must not require it.
- Running MCP inside the WinForms process would add a server lifecycle, threading and a network-like surface to the editor. A separate process that reads an **explicitly published** snapshot keeps the host unchanged and testable.

## Shape
```
TE2 Enhanced (net48 host)                    TE2Enhanced.Mcp (net8.0 exe, roll-forward, stdio)
  PbiBench > Publish read-only snapshot  ──►  %LOCALAPPDATA%\TE2Enhanced\mcp\snapshot-v1.json  ──►  MCP client
  (explicit user action, atomic replace)       (PbiBench.Core ModelSnapshotV1, metadata only)
```
- Contract: `PbiBench.Core/Snapshot/ModelSnapshotV1.cs`, `ContractVersion = 1`. Unknown versions are refused.
- Contents: model name, opaque `modelRevision`, `publishedAtUtc`, compatibility level, tables/columns/measures (name, folder, format string, hidden, description), relationships, selection. **Measure expressions only when the user opts in** (`IncludesExpressions`); a snapshot carrying expressions without the flag is refused.
- Not exported by design: connection strings, server names, credentials, data rows (the publisher never reads them). Text the user typed into the model (names, descriptions, folders, format strings, expressions) is copied as is, so a **heuristic credential guard** runs on write and on read: every string in the snapshot (model name, producer, revision, publication time, tables, columns incl. data types and folders, measures incl. opted-in expressions, relationships, selection) is checked case-insensitively, with optional spaces around `=` or `:`, against these shapes: `password`/`passwd`/`pwd` with a value; `User ID`/`uid` with a value; `AccountKey` with a value; `client_secret` (also `client secret`, `client-secret`); `SharedAccessSignature=`; `Bearer <token of 8+ characters>`; `Authorization:` with a value; a `?sig=`/`&sig=` query parameter; and the connection-string shape `Data Source=...;`, `Server=...;`, `Address=...;`. A match refuses the whole snapshot (publish fails, the companion returns a tool error). This is a heuristic, not a guarantee: secrets in other shapes are not detected. 32 MiB cap.
- Malformed snapshots (garbage bytes, null list elements, null strings, wrong types) are refused as a tool error; the server keeps serving.
- Staleness is explicit: `get_model_summary` returns `publishedAtUtc`, `ageSeconds` and `modelRevision`. The server reloads when the file changes.

## Protocol
Newline-delimited JSON-RPC 2.0 over stdio. Protocol versions `2025-06-18` (default), `2025-03-26`, `2024-11-05`. Methods: `initialize`, `ping`, `tools/list`, `tools/call`, notifications ignored. Logs go to stderr only. No resources, prompts, sampling or network.

| Tool | Arguments | Notes |
|---|---|---|
| `get_model_summary` | — | counts, revision, age |
| `list_tables` | `filter?`, `includeHidden?` (default true) | |
| `list_measures` | `table?`, `filter?`, `limit?` 1–500 (default 100) | returns `total`, `truncated` |
| `get_measure` | `table`, `name` | `expression` null and `expressionWithheld` true without opt-in |
| `list_relationships` | `table?` | |
| `get_selection` | — | selection at publication time |

All tools carry `readOnlyHint: true`, `openWorldHint: false`. Unknown tools (for example any write) are JSON-RPC `-32602`. Arguments are type-checked: a wrong type (for example `"limit":"5"`, `"includeHidden":"no"`, a non-string `name` or `protocolVersion`, non-object `params`/`arguments`) is `-32602`; a non-string `method` or a structured `id` is `-32600`. Each request is handled in isolation: no request stops the server, and internal failures are `-32603` without details on stdout.

## Running
```powershell
dotnet run --project TE2Enhanced.Mcp/TE2Enhanced.Mcp.csproj -c Release -- --self-test
dotnet run --project TE2Enhanced.Mcp/TE2Enhanced.Mcp.csproj -c Release -- --snapshot C:\path\snapshot-v1.json
```
Snapshot path resolution: `--snapshot`, then `TE2ENHANCED_MCP_SNAPSHOT`, then `%LOCALAPPDATA%\TE2Enhanced\mcp\snapshot-v1.json`. Client registration (for example in a Claude/VS Code MCP config) points `command` at the built `TE2Enhanced.Mcp.exe`; it is the user's choice, nothing is registered automatically.

## Acceptance (independent path)
- `--self-test` (Verify stage `mcp-smoke`): negotiation, notification silence, 6 read-only tools, missing snapshot message, counts, staleness age, bounds, unknown table/measure, expression withholding and opt-in reload, parse/method/unknown-tool errors, credential and contract guards, a real stdio round trip of the built executable.
- Light QA / owner: publish from the host on a synthetic model, register the exe in one MCP client, list measures, confirm no expression without opt-in, close TE2 and confirm the companion still answers from the snapshot.

## V2 (not built): approved edits
Edits would be **proposals**, not mutations: the companion writes a proposal file (recipe-shaped, like AUTO-002), TE2 shows it in the existing reviewed-recipe UI with diff, stale check against `modelRevision`, apply in one undo batch, and reports the outcome back. No silent apply, no arbitrary C#. Decision deferred until the read-only path is accepted.
