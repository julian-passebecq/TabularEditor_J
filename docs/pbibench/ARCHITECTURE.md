# PbiBench architecture contract

## Mission

PbiBench is a focused Power BI engineering IDE built on the Tabular Editor 2 semantic engine. It is not a general Microsoft Fabric administration application.

The product mental model is:

```text
Power BI Desktop / PBIP
        <->
      PbiBench
        |
        +-- MODEL       semantic model / TOM / TE2
        +-- DAX         authoring, query, validation, formatting
        +-- EXPLORE     preview/profile/pivot for model work
        +-- AUTOMATE    C# scripts, safe recipes, gallery
        +-- OPTIMIZE    BPA, model quality, storage evidence
        +-- REPORT      PBIR, themes, report lineage
        +-- PROJECT     PBIP, TMDL, Git, recovery, context export
```

## Runtime ownership

### TE2 upstream/core

Keep and synchronize:
- TOMWrapper and model object behavior;
- undo/redo and dependency infrastructure;
- existing DAX expression editor;
- existing C# scripting engine;
- BPA foundation;
- serialization and Power BI Desktop connection support.

### PbiBench-owned code

New capabilities should live under PbiBench-owned projects/namespaces wherever practical. Prefer adapters around TE2/TOM instead of modifying upstream internals.

The first neutral library is `PbiBench.Core` (`netstandard2.0`). It owns contracts and pure project logic only and has no dependency on WinForms, TE2/TOM, Fabric, authentication, HTTP or AI providers.

Later host adapters may target `net48` and reference TE2/TOM.

### Report engineering

PBIR/report code is part of the PbiBench product because it is source/project engineering adjacent to PBIP/TMDL and semantic authoring. It may use a modern isolated process internally if required, but should be presented to the user as the REPORT module of PbiBench.

### Fabric Toolbox

Separate application and roadmap. It owns Fabric platform administration such as workspaces, OneLake, jobs, capacities, permissions, REST and PowerShell. PbiBench may launch it with a project/workspace context file, but must not absorb its auth/runtime.

### MCP Companion

Future optional separate application. It consumes project-context JSON and configures/launches external MCP hosts. No core PbiBench feature may require it.

## External specialist boundaries

- DAX Studio: deep Server Timings/query plans. Keep a handoff only.
- Bravo: optional external model helper.
- Power BI Desktop: final rendering and supported Desktop workflows.
- VS Code: source/Git editing.
- Theme Forge: visual design simulator; exchanges model-context/dashboard-spec/theme JSON.

## Mutation principles

For new PbiBench write paths use:

```text
inspect -> immutable proposal/diff -> explicit user review -> apply -> undo/backup/recovery
```

Do not create hidden remote writes or provider-dependent automation.

## Project Context

`pbibench-project-context.json` is provider-neutral. It may contain local project paths, PBIP/TMDL/PBIR identities and bounded Git state. It must not contain credentials, access tokens, connection strings, gateway secrets or raw business rows.

## DAX formatter

TE2 already has DAX Formatter integration (F6 / Ctrl+F6) and batch scripting support. That path sends DAX to the SQLBI DAX Formatter service. PbiBench must not present it as offline/local.

The future PbiBench DAX surface should make formatter provenance/privacy explicit and may add a formatter-provider abstraction, but must preserve the existing TE2 behavior until a tested replacement exists.

## Non-goals

- full Visual Studio replacement for C#;
- full Git client;
- DAX Studio clone;
- Fabric administration in the core app;
- embedded ChatGPT/Claude/MCP runtime;
- arbitrary unsafe PBIR JSON writer;
- TE3 proprietary implementation copying.
