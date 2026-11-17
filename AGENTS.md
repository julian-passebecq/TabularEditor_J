# TE2 Enhanced (TabularEditor_J)

A fork of Tabular Editor 2 (WinForms, C#) plus TE2 Enhanced, a lighter Power BI engineering add-on; some internal names still say `PbiBench`. The `upstream` remote is the original project. `README_PBIBENCH.md` describes the add-on; `projectmanagement/` and `handover/` hold its plan and history.

## Build and test
- `Scripts/Verify-PbiBench.ps1` builds and runs the verify stage with a pinned .NET SDK; `PbiBench.Core.Smoke` and `PbiBench.Host.Smoke` are the smoke projects. `Scripts/ci/Package-PbiBench.ps1` packages it, and the MCP self-test is `TE2Enhanced.Mcp.exe --self-test`.
- CI: GitLab mirror `juliandatapass-group/tabulareditor-j`, job `verify` on the `local-windows` runners (installs its own .NET SDK). The default branch is `master`.

## Guidelines
- Keep changes to upstream code small and the TE2 host recognizable, so merges from upstream stay easy. Nothing is pushed to upstream.
- `PbiBench.Core` stays neutral: no WinForms, TOM, AMO, HTTP or auth imports; host adapters live in the TE2 integration area.
- Disk, Loaded, Live, Git and Baseline are distinct identities; nothing silently overwrites another. Unknown report schemas stay read-only, and a bulk write needs a reviewed proposal plus undo, because it edits real models.
- No proprietary Tabular Editor 3 code or assets.
- Live Desktop, XMLA or sign-in checks without evidence stay open, not passed.

How work flows: the fleet guideline every session loads (`~/.claude/CLAUDE.md`, `~/.codex/AGENTS.md`). CI recipe and runners: datapass-fleet-facade `AGENTS.md`.
