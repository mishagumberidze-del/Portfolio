# Verified findings

Figures below were checked on 2026-11-03 against the untouched source CSV, SQL core/reporting views and the SQL-produced booking extract. The regenerated DAX uses these definitions. Current Desktop refresh, page review and City Hotel filter evidence are recorded in [native validation](qa/powerbi_native_validation_2026-11-05.md); older images under `powerbi/qa/` predate this audit.

- 119,390 bookings; 44,224 canceled, a 37.04% cancellation rate.
- The eligible room value proxy is **25,996,324.21 CU**, using non-canceled, positive-night bookings with nonnegative ADR. It is not audited revenue.
- **Stay-weighted ADR is 101.93 CU**: eligible room value divided by **255,030 eligible stay nights**. The previous 100.90 figure was an unweighted average of booking ADRs.
- There are **74,486 non-canceled positive-night bookings** with 255,040 stay nights. One negative-ADR booking is retained for stay-volume analysis but excluded from the rate/value population.
- Assigned room type differs from reserved type on **13,744** of these stays (18.45%). This does not establish an actual room move.
- The repeat-guest flag appears on 3,810 bookings (3.19% of all bookings); the file cannot identify unique repeat people.
- City Hotel has 79,330 bookings and 33,102 cancellations; Resort Hotel has 40,060 bookings and 11,122 cancellations.
- The `Groups` segment has a 61.1% observed cancellation rate versus 15.3% for `Direct`. These associations may reflect booking-process differences; they do not establish causes.
