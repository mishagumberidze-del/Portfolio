# SQL model and lineage

- `stg.Booking`: all 32 original fields as text plus `SourceRow`; preserves every source record.
- `core.Hotel` (`HotelId` PK) and `core.MarketSegment` (`MarketSegmentId` PK): observed source categories.
- `core.Date` (`DateKey` PK): observed arrival dates, 793 distinct days.
- `core.Booking` (`BookingId` PK, `SourceRow` unique): typed booking fact. Foreign keys reference Hotel, MarketSegment, and Date.
- `rpt.BookingAnalysis`: report-friendly fact with hotel, segment, date, lead-time band, and stay band.
- `rpt.MonthlyPerformance` and `rpt.SegmentPerformance`: ready-to-use aggregates.
- `etl.LoadAudit`: hash, row counts, flags, duplicate count, and load timestamp.

Derived features: stay nights, gross room value proxy, realized room value proxy, room-changed flag, data-quality flag, lead-time band, and stay band. All row-level source fields remain in staging; the core retains relevant operational attributes. The load deletes and rebuilds core data in a transaction, then checks that all staging rows arrived. A second load produced the same 119,390 core rows.
