# Engine revision

| Item | SSAS (local service) | Power BI Desktop |
|---|---|---|
| Product | Microsoft SQL Server Analysis Services (RTM, Developer64) | Desktop Store package 2.158.1304.0 (msmdsrv 2025.0170.0087.18, releases/2026/08.4) |
| Server.Version | 16.0.42.229 (SSAS 2022 line) | 17.0.87.18 |
| Server mode | Tabular | SharePoint (reported by the embedded engine) |
| Model compatibility level | 1600 | 1606 (compatibilityMode PowerBI) |
| DBMSVersion row | empty on this engine (Server.Version used) | 17.0.87.18 |

Fixture compatibility level: 1600. Supported by both: SSAS 16.x runs 1600 natively; Desktop opened the fixture and upgraded its in-memory model to 1606 (Desktop always hosts its own level).

Meaning for the oracle: the engines are two different builds (SSAS 2022 16.x vs Desktop AS 17.x, about one generation newer, new compat level). The fixture uses only core DAX (SUM, AVERAGE, DIVIDE, RANKX, CONCATENATEX, CALCULATE on a calculated DATATABLE), whose semantics are stable across these builds; all 6 queries returned identical results, so no engine-specific divergence is observed for this set. It says nothing about newer DAX functions or Desktop-only features (calculation groups on 1606, DirectLake, etc.). SSAS needed PowerBI_V1 as data source version (V3 rejected), which is irrelevant to a calculated table.

Result: PASS - both revisions read and recorded, compatibility level supported on both.
