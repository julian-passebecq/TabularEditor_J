# DAX formatting in the TE2-based PbiBench

## What the clean upstream already has

Tabular Editor 2 already exposes DAX formatting in the expression editor:

- `F6` — format DAX (long format)
- `Ctrl+F6` — short-line format
- scripting helpers such as `FormatDax(...)` / batch formatting.

The implementation calls the SQLBI DAX Formatter web service. It is not an offline formatter.

## Current SQLBI guidance (verified 2026-09)

SQLBI documents `Dax.Formatter` as the supported .NET client and recommends batching multiple expressions in one request. The client is MIT licensed and compatible with .NET Standard 2.0 / .NET Framework 4.8, but the formatting logic still runs server-side at `api.daxformatter.com`.

Therefore simply adopting the newer package would modernize transport/diagnostics, but would **not** create local/offline formatting.

## PbiBench direction

### v0.1
Preserve upstream behavior and make the privacy/capability facts explicit in `PbiBench.Core.Dax.DaxFormatterCapabilities`.

### v0.2
Add a small formatter-provider boundary around the DAX workbench:

```text
IDaxFormatter
  ProviderName
  RequiresNetwork
  SendsDaxOffDevice
  FormatSingleAsync
  FormatBatchAsync
```

Initial provider may wrap the existing TE2/SQLBI path rather than changing behavior.

UI should show something like:

`Format DAX · SQLBI remote`

with a one-time/hover explanation that DAX text is sent to the formatter service.

### Offline formatter
Only add an `Offline` provider after identifying a compatible formatter whose formatting engine actually runs locally and whose license/update model is acceptable. Do not label the SQLBI NuGet client or MCP wrapper as offline: both call the same hosted formatting service.

## DAX Studio
DAX Studio remains external. Formatting is an editor concern; Server Timings/query plans remain a DAX Studio concern.
