# Upstream Tabular Editor 2 sync policy

## Upstream

Repository: `TabularEditor/TabularEditor`

Current pinned baseline for PbiBench foundation:
`7029129aa3f45d35f987d8f6ac7e5a971f28771c`

At branch creation, `julian-passebecq/TabularEditor_J:master` matched that exact upstream SHA.

## Rule

PbiBench must remain easy to rebase/sync when TE2 receives compatibility or critical fixes.

### Prefer

1. additive PbiBench projects/folders;
2. adapters and composition roots;
3. tiny shell hooks;
4. explicit project references;
5. generated/owned UI surfaces instead of large edits to upstream WinForms Designer files.

### Avoid

- mass-renaming TabularEditor/TOMWrapper namespaces;
- rewriting upstream project layout;
- moving upstream files merely for aesthetics;
- large FormMain.Designer.cs diffs;
- copying Fabric/MCP runtime dependencies into the TE2 executable;
- package upgrades unrelated to a PbiBench feature.

## Upstream-change ledger

Every PbiBench pass that edits an upstream-owned file must append to this table.

| PbiBench pass | Upstream file | Reason | Expected conflict risk |
|---|---|---|---|
| v0.1 foundation | none | additive core/docs only | none |

## Recommended sync procedure

```text
1. fetch upstream master
2. record old upstream pin
3. compare old pin -> new upstream pin
4. update fork master with upstream only
5. rebase/merge PbiBench branch onto new master
6. inspect the upstream-change ledger first
7. run upstream TE2 tests/build
8. run PbiBench core/host tests
9. update this document with the new pin
```

Do not resolve conflicts by blindly keeping the PbiBench side. TE2 behavior is the semantic foundation; PbiBench hooks should adapt around it.

## Provenance

TE2 remains MIT licensed under the repository root `LICENSE`. New PbiBench-owned code should preserve clear provenance and must not copy Tabular Editor 3 proprietary code/assets/internal implementation.
