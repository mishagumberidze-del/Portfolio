# Verification — 2026-11-02

- SQL source, core, and reporting rows reconcile at 119,390; cancellations reconcile at 44,224 and room value proxy at 25,996,260.41 CU.
- A fresh-folder raw-to-staging build matches the existing staging file byte for byte; source hashes remain unchanged.
- The self-contained PBIP generator was run twice in isolation. Its generated files match byte for byte and it defines DataRoot exactly once. It does not depend on the Olist project.
- All 27 visuals fit their 1920 × 1080 pages. Three filters are present on each page; the trend groups dates by year/month.
- The rebuilt PBIP was opened and refreshed in Power BI Desktop. All three pages rendered without error, and City Hotel selection changed the KPI to approximately 14.39M room value proxy, matching SQL's 14,394,410.18. The filter was cleared, the overview restored, and the refreshed PBIP saved.
- Desktop review captures are in `powerbi/qa/`. They include the Desktop interface and are review evidence, not standalone canvas exports.

Run the workspace `verify_rebuild.py` using the same Python environment to repeat file-rebuild checks. A new machine still needs SQL Server, its client tools, Python dependencies, and Power BI Desktop as documented in the README; verification did not reinstall those applications.
