# SQL model and lineage

- `stg.Booking`: all 32 original fields as text plus `SourceRow`; preserves every source record.
- `core.Hotel` (`HotelId` PK) and `core.MarketSegment` (`MarketSegmentId` PK): observed source categories.
- `core.Date` (`DateKey` PK): observed arrival dates, 793 distinct days.
- `core.Booking` (`BookingId` PK, `SourceRow` unique): typed booking fact. Foreign keys reference Hotel, MarketSegment, and Date.
- `rpt.BookingAnalysis`: report-friendly fact with hotel, segment, date, lead-time band, and stay band.
- `rpt.MonthlyPerformance` and `rpt.SegmentPerformance`: ready-to-use aggregates.
- `etl.LoadAudit`: hash, row counts, flags, duplicate count, and load timestamp.

Derived features: stay nights, gross room value proxy, realized room value proxy, assigned/reserved room-type difference flag, data-quality flag, lead-time band, and stay band. All row-level source fields remain in staging; the core retains relevant operational attributes. The load deletes and rebuilds core data in a transaction, then checks that all staging rows arrived. A second load produced the same 119,390 core rows.

## Reporting populations and purpose

| Measure | Definition and question |
|---|---|
| Bookings, cancellations, cancellation rate | All observed bookings; how much booked demand cancels? |
| Room value proxy | Sum ADR × nights only for non-canceled, positive-night, nonnegative-ADR bookings; what booked room value remains under this eligibility rule? |
| Stay-weighted ADR | The same eligible value divided by the same eligible stay nights; what rate applies per booked stay night? |
| Stayed Bookings, Stay Nights and average stay nights | Non-canceled, positive-night bookings; how many stays and booking stay-nights belong to this arrival cohort, and how long is the average stay? |
| Room Assignment Difference | Assigned type differs from reserved type, divided by stayed bookings; how often does the final room type differ? No actual room move is inferred. |
| Average Lead Time | Booking lead days over all selected bookings; contextualizes the cancellation horizon. |

The repeat-guest flag and quality flags remain available in the source and audit checks but are not report KPIs. They do not directly answer the operations page's stay-volume question. The source cannot establish unique customer retention from the repeat flag.

All stay nights are attributed to the booking's arrival period, including nights that cross a month boundary. They describe the arrival cohort's total stay volume; they are not actual occupied room-nights by calendar date and do not establish required staffing, occupancy or RevPAR. See [KPI usefulness](kpi_usefulness.md) for placement and decision limits.

`rpt.MonthlyPerformance` and `rpt.SegmentPerformance` expose `StayWeightedADR` with matched numerator and denominator. Do not average their rates across groups; sum eligible value and eligible nights before dividing. The Power BI measures calculate from booking facts and use `KEEPFILTERS` so a conflicting visual selection does not silently replace the selected population.

Core `GrossRoomValue` and `RealizedRoomValue` retain the original signed row derivations for traceability. The displayed proxy adds eligibility rules; the core columns alone are not the audited report KPI. Invalid ADR does not remove a booking from unrelated cancellation or stay-length measures.
