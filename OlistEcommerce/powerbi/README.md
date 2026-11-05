# Power BI report

Open `OlistEcommercePBIP/OlistEcommerce.pbip` in Power BI Desktop.

## Completed implementation

- Five 1920×1080 business pages plus a customer drill-through page.
- Import-mode semantic model with 14 tables, 120 columns, 47 DAX measures, and nine single-direction relationships.
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
- `qa/`: pre-audit visual previews from an earlier Power BI Desktop refresh. These images do not validate the reviewed source definitions.

Earlier-build PBIR and TMDL validation is recorded in the project history. It does not replace a Power BI Desktop refresh and visual review of the current audited definitions.
