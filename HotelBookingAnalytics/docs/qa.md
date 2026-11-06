# Verification — native completion 2026-11-05

The corrected PBIP was refreshed and saved in Power BI Desktop. All three pages were reviewed, and a City Hotel filter displayed approximately 79K bookings and 106.50 CU stay-weighted ADR, matching the independently checked City Hotel population. [Native validation and current screenshots](qa/powerbi_native_validation_2026-11-05.md) record the observed check. Earlier failed launches and the circular-dependency diagnosis are retained as historical evidence.

# Calculation audit — 2026-11-03

- Live SQL source/core checks passed: 119,390 bookings, 44,224 cancellations. Only reporting-view definitions were updated; no source, staging or core data was reloaded.
- The eligible room value independently reconciles from staging ADR/night fields to `rpt.MonthlyPerformance` at 25,996,324.21 CU. Rate denominator: 255,030 nights; stay-weighted ADR: 101.93 CU.
- `tests/verify_metrics.py` checks all raw/export booking facts as a multiset, preserving duplicate source records. It reconciles 39 hotel/year/segment groups, cancellation, stay, room-assignment, repeat-flag and quality populations. A constructed fixture checks unequal stay weights and exclusion of canceled, zero-night and negative-rate rows from the rate.
- The PBIP was regenerated with 10 measures, three pages and 27 visuals. Source formulas keep filter intersections and use matching rate/value populations. Operations headlines now show stay count, booking stay-nights, average stay length and room-assignment difference; repeat/quality flags remain in audit data rather than the operational KPI cards.
- The file-based tests and SQL checks do not execute the regenerated DAX engine or refresh the Desktop cache. The existing images in `powerbi/qa/` show the pre-audit report; Desktop visual verification was still outstanding at that stage; the successful later check is recorded above.

Run `python tests/verify_metrics.py`, `sqlcmd -S .\SQLEXPRESS -E -d Portfolio_HotelBookings -b -i sql/03_validate.sql`, and the workspace `verify_rebuild.py` to repeat the relevant checks.

## Earlier build evidence — 2026-11-02

The earlier full pipeline, isolated raw-to-staging build, deterministic PBIP rebuild and Power BI Desktop refresh were verified. All 27 visuals fit 1920 × 1080 pages, and a City Hotel filter matched the then-current SQL KPI. That evidence describes the earlier formulas and is not a new Desktop validation of this audit.
