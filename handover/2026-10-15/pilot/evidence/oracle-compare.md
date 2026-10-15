# Oracle compare: SSAS 2022 (16.x) vs Power BI Desktop (AS 17.x)

Rule: numbers exact, decimals within 1e-9, column names compared as given.

| Query | SSAS rows | Desktop rows | Columns equal | Cells equal | Errors | Match |
|---|---|---|---|---|---|---|
| Q1 | 1 | 1 | True | True | False | MATCH |
| Q2 | 3 | 3 | True | True | False | MATCH |
| Q3 | 1 | 1 | True | True | False | MATCH |
| Q4 | 1 | 1 | True | True | False | MATCH |
| Q5 | 3 | 3 | True | True | False | MATCH |
| Q6 | 1 | 1 | True | True | False | MATCH |

Result: PASS - all 6 queries match, none errored on either side

Notes: Q6 Blank and DIVIDE(1,0) are null on both. SSAS fixture was deployed with defaultPowerBIDataSourceVersion PowerBI_V1 (V3 rejected by SSAS); Desktop used the fixture as is. Both models use the same TMDL tables/measure, so the data source version does not affect these calculated-table queries. Same query text and ids verified on both sides.
