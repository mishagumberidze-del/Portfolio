# Native Power BI validation — 2026-11-05

The corrected Hotel PBIP was opened, refreshed and saved in Power BI Desktop after the circular sort dependency fix. Executive Overview, Cancellation Patterns and Stay & Operations were rendered and reviewed.

A City Hotel filter recalculated the displayed booking count to approximately 79K and stay-weighted ADR to **106.50 CU**, matching the independently checked City Hotel population (79,330 bookings). The report was then saved with the reviewed pages. This verifies the observed native render and filter behavior; it is not an exhaustive test of every possible filter combination.

Current evidence:

- [Executive Overview](reviewed-executive-2026-11-05.png)
- [City Hotel filter](reviewed-city-filter-2026-11-05.png)
- [Cancellation Patterns](reviewed-cancellations-2026-11-05.png)
- [Stay & Operations](reviewed-operations-2026-11-05.png)

The existing raw/export/SQL checks remain the evidence for exact full-population values and eligibility rules. The [earlier runtime handoff](powerbi_runtime_handoff_2026-11-05.md) preserves the circular-dependency diagnosis and interrupted attempts; those earlier failures were resolved for the native checks recorded here. Screenshots under `powerbi/qa/` remain historical previews.
