# OBS-001 probe (2026-10-13)

`Scripts/diagnostics/Probe-FileReplace.ps1`: for each of N iterations, write `dest`, write `dest.tmp`, then either `File.Replace(tmp, dest, null)` or `Delete(dest)+Move(tmp, dest)`. Synthetic files only.

| Location | File.Replace (.dat) | File.Replace (.json) | Delete+Move |
|---|---|---|---|
| `%TEMP%` on C: | 3 / 500 fail | 4 / 500 fail | 0 / 500 |
| worktree `.tools` on D: | 0 / 500 | 0 / 500 | 0 / 500 |

The failure is always `IOException: Unable to remove the file to be replaced` (ERROR_UNABLE_TO_REMOVE_REPLACED, 1175), which matches every recorded OBS-001 occurrence: DAX save, CSV export, ReviewedFilePlan commit and MCP snapshot.

**Interpretation (likely, not proven):** another process briefly holds a newly created file under C:\...\Temp without delete sharing. Real-time antivirus or indexing scans are the usual cause. The probe cannot identify the process; Process Monitor or the Defender exclusion list would. This is an owner action and touches security settings, so it was not done.

**Product impact:** user files saved in their own folders are probably less exposed than `%TEMP%`, but not immune. Every product path fails without data loss:
- the original file is kept;
- ReviewedFilePlan rolls back;
- the dialogs report the failure.

No product retry was added.

**Smokes:** the theme smokes, the S5/fix Core smokes and the MCP self-test tolerate exactly one recorded occurrence per run and fail on a second.

**Decision for the owner / Light QA:** keep it as is (fail visibly), or add a bounded retry only for error 1175 in the product writers. A retry would trade a visible failure for a short delay; it is a product decision, not a test fix.
