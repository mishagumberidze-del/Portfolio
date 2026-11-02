# Verified findings

SQL queries use `Portfolio_HotelBookings.rpt.BookingAnalysis`; figures were checked against the refreshed Power BI report.

- 119,390 bookings; 44,224 canceled, a 37.04% cancellation rate.
- Non-canceled `ADR × stay nights` totals 25,996,260.41 source currency units. This is a room value proxy, not audited revenue.
- Realized ADR, excluding canceled, zero-night, and negative-ADR rows, is 100.90.
- City Hotel has 79,330 bookings and 33,102 cancellations; Resort Hotel has 40,060 bookings and 11,122 cancellations.
- The `Groups` market segment has a 61.1% observed cancellation rate versus 15.3% for `Direct`. This is association and may reflect booking process differences.
