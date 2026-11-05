# Final browser pass — 2026-11-05

The final report uses a linear 0–100% retention color scale with a legend, replacing a scale that saturated above 10.7%. The browser shows 30 distinct populated shades. Charts preserve readable labels in horizontally scrollable, keyboard-focusable regions on phones. Headline values match the audited figures; desktop 1440 px and phone 390 px have no page overflow. The isolated HTML/summary rebuild passed.

Current evidence: [browser results](../report/qa/browser-results-2026-11-05.json), [desktop](../report/qa/reviewed-desktop.jpg), [phone](../report/qa/reviewed-mobile.jpg). Older checks below describe earlier revisions.

# Calculation audit — 2026-11-03

- `tests/verify_metrics.py` passed against all 541,910 original CSV lines and the SQL exports. It independently reconciles quantities, dates, exact values, customer frequency, customer concentration, invoice distribution and 90-day eligibility/numerators.
- Gross / credits / net remain 10,666,702.54 / 896,812.49 / 9,769,890.05 CU. There are 4,338 identified purchasers; the top decile contains 434 customers.
- The fixed 90-day groups reconcile to 527/885, 560/1,249, 329/826 and 191/410 repeated/eligible customers for 2010Q4 through 2011Q3 respectively.
- The headline pools these counts to 1,607/3,370 (47.69%), rather than averaging quarter rates or comparing customers with unequal follow-up. The independent raw-source test verifies both counts and the pooled rate.
- Removed RFM/quartile segments and model-based reference intervals from the report. Concentration now includes credits and net value; repeat comparisons show their actual counts.
- The regenerated HTML and summary match an isolated rebuild byte for byte. The new HTML has not received browser visual verification. Existing preview images show the earlier report.

## Earlier build evidence

The following notes describe the previous report and are retained as historical evidence, not verification of the revised presentation.

# Verification — 2026-11-02

- The complete `build_all.ps1` workflow ran against the existing SQL Server Express instance: validation, staging, relational reload, source reconciliation, reporting exports, and HTML generation.
- Source, core, and reporting rows reconcile at 541,910. All-line amount reconciles to the source at 9,747,765.9340 CU, including negative and zero prices. Analytical gross/return/net values follow the report's narrower positive-price definitions: 10,666,702.5440 / 896,812.4900 / 9,769,890.0540 CU.
- Four prices of 0.001 are preserved. The load fails if source/core totals or zero-price classifications differ.
- A fresh-folder staging build matches byte for byte; the original source hash is unchanged. A separate HTML build from SQL exports matches both HTML and analysis summary byte for byte.
- Desktop and full-length previews were rendered locally in Chrome. The entire report was visually reviewed, including RFM, cohorts, reference intervals, and methodology. The narrow layout was checked at a browser viewport of 504 px: document scroll width equals viewport width and no report sections extend outside it. Wide tables scroll within their panels.
- The standalone report uses inline styles and SVG and has no network dependencies. Cohorts omit incomplete December 2011; the 90-day analysis uses only customers with enough follow-up time.

Preview files are in `report/`. Reference intervals use a stated binomial model; the dataset is not a random population sample. Recommendations are proposed actions to evaluate, not measured commercial results.
