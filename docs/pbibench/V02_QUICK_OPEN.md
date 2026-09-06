# PbiBench v0.2 — Quick Open core

This slice starts the v0.2 MODEL + DAX work without coupling the neutral core to the Tabular Editor WinForms shell or TOM assemblies.

## What is implemented

`PbiBench.Core.Navigation` now defines:

- a provider-neutral semantic object descriptor;
- semantic object kinds for tables, measures, columns, hierarchies, relationships, calculation groups/items, functions, perspectives, roles, partitions and data sources;
- deterministic Quick Open ranking;
- exact, prefix, word-prefix, contains and subsequence matching;
- additional search aliases/terms;
- `kind:` filters such as `kind:measure`, `kind:table`, `kind:column`, `kind:fn`, `kind:ci`;
- visible-object preference while keeping hidden objects searchable;
- bounded result lists.

Examples:

```text
revenue
net sales
kind:measure revenue
kind:table sales
kind:fn currency
```

## Why this lives outside the TE2 UI

The existing TE2 tree/search dialogs are useful but are tightly coupled to WinForms and TOM. PbiBench needs one search model that can eventually index:

1. the currently loaded TOM model;
2. TMDL/PBIP artifacts on disk;
3. report-lineage references;
4. Git/diff findings.

The neutral descriptor is the contract between those sources and the UI.

## Next integration slice

The shell adapter should project TE2 TOM objects into `SemanticObjectDescriptor` and expose a `Ctrl+P` Quick Open surface. Selecting a result should use the existing TE2 navigation/Goto path rather than inventing a second navigation system.

The same adapter can later add report usage and issue badges without changing the matcher.

## Safety / scope

This slice is read-only. It does not mutate a semantic model, PBIP project or report, and it does not add any network dependency.
