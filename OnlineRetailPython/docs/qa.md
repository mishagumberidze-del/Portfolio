# Verification — 2026-11-02

- The complete `build_all.ps1` workflow ran against the existing SQL Server Express instance: validation, staging, relational reload, source reconciliation, reporting exports, and HTML generation.
- Source, core, and reporting rows reconcile at 541,910. All-line amount reconciles to the source at 9,747,765.9340 CU, including negative and zero prices. Analytical gross/return/net values follow the report's narrower positive-price definitions: 10,666,702.5440 / 896,812.4900 / 9,769,890.0540 CU.
- Four prices of 0.001 are preserved. The load fails if source/core totals or zero-price classifications differ.
- A fresh-folder staging build matches byte for byte; the original source hash is unchanged. A separate HTML build from SQL exports matches both HTML and analysis summary byte for byte.
- Desktop and full-length previews were rendered locally in Chrome. The entire report was visually reviewed, including RFM, cohorts, reference intervals, and methodology. The narrow layout was checked at a browser viewport of 504 px: document scroll width equals viewport width and no report sections extend outside it. Wide tables scroll within their panels.
- The standalone report uses inline styles and SVG and has no network dependencies. Cohorts omit incomplete December 2011; the 90-day analysis uses only customers with enough follow-up time.

Preview files are in `report/`. Reference intervals use a stated binomial model; the dataset is not a random population sample. Recommendations are proposed actions to evaluate, not measured commercial results.
