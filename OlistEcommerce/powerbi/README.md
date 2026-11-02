# Power BI report

Open `OlistEcommercePBIP/OlistEcommerce.pbip` in Power BI Desktop.

## Completed implementation

- Five 1920×1080 business pages plus a customer drill-through page.
- Import-mode semantic model with 14 tables, 124 columns, 59 DAX measures, and nine single-direction relationships.
- Dedicated `Date` and `KPI Measures` tables; hidden technical keys; role-playing customer and seller geography dimensions.
- Dynamic trend metric selector, seller Top-N selector, Top-25 product detail filter, cohort matrix, cross-context DAX bridges, and customer drill-through.
- Restrained navy/teal/gold theme with consistent headers, slicers, KPI cards, spacing, and typography.
- Mixed-unit comparisons are separated; highly skewed categorical outcomes use tables or donut shares so small values remain readable.

## Data flow

Power BI does not read `data/raw/`. `etl/export_powerbi.ps1` exports ten headerless UTF-8 TSV files from clean `core` tables and `rpt` views in `Portfolio_Olist`; hashes and row counts are recorded in `data/processed/powerbi/manifest.json`.

```powershell
& ..\etl\export_powerbi.ps1
```

The semantic-model parameter `DataRoot` points to the local processed-export folder. Change it if the project is moved to another computer, then refresh.

## Source and QA artifacts

- `OlistEcommercePBIP/`: final PBIP, PBIR, and TMDL source.
- `build_report.mjs`: deterministic report-definition generator.
- `theme.json`: source theme.
- `model_relationships.md`: model-grain and relationship notes.
- `measures.dax`: human-readable measure reference; canonical executable definitions live in `OlistEcommerce.SemanticModel/definition/tables/KPI Measures.tmdl`.
- `qa/`: page screenshots captured from the refreshed Power BI Desktop report.

The PBIR validator reports zero structural errors. The only offline warnings are skipped online JSON-schema downloads. The TMDL model passes parser and live Analysis Services metadata validation.
