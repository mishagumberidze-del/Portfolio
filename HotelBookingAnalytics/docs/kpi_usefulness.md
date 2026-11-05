# Why these metrics are in the report

The report supports historical booking, cancellation and stay-mix review. It does not claim to optimize staffing or forecast occupancy from fields the dataset does not contain.

| Business question | Useful measure | How to use it | Boundary |
|---|---|---|---|
| How much booked demand is lost to cancellation? | Booking count, cancellation count and rate by segment, lead time and channel | Prioritize segments for a booking-process investigation; inspect counts alongside rates | Observed associations do not identify causes or predict an individual cancellation |
| How do the retained bookings differ in value and rate? | Eligible room-value proxy and stay-weighted ADR | Compare the scale and nightly booked rate of comparable hotel/segment populations | Proxy based on ADR × nights; no recognized revenue, costs or margin |
| How large and long are the non-canceled stays? | Stayed Bookings, Stay Nights and average stay nights | Compare the size and stay-length mix of arrival cohorts | Booking-grain nights attributed to arrival dates, not nightly occupied rooms or capacity utilization |
| How often does the final room type differ from the reserved type? | Room Assignment Difference, with stay counts | Identify room categories worth examining for allocation or booking-process differences | A difference is not proof of a room move, upgrade, service failure or added cost |
| How far in advance were these bookings made? | Average lead time and cancellation rates by lead-time band | Context for the booking horizon and cancellation comparisons | Lead time alone does not establish a safe cancellation policy |

Removed from the operational headline: repeat-guest booking share and flagged-booking count. The first cannot identify unique repeat customers; the second is a data-quality diagnostic. Both remain auditable without taking the place of useful stay-volume measures.

Not calculated: occupancy, RevPAR, profit, guest lifetime value or forecast staffing. Room capacity, realized financials, durable guest identity and a day-by-day operational roster are absent.
