# Final KPI logic review — 2026-11-05

The retained measures answer specific questions supported by the available data. A measure's presence is justified by its use, not by how sophisticated the formula looks. These historical portfolio datasets do not supply business targets, so no success thresholds or causal impact are invented.

| Project | Question the main measures answer | Main measures retained | Removed or demoted |
|---|---|---|---|
| Hotel | How much booking demand cancels, what remains in booked room value, and how large/long are the non-canceled arrival cohorts? | Bookings and cancellation rate; eligible room-value proxy and stay-weighted ADR; stayed bookings, stay-nights, average length and room-assignment difference | Repeat-guest flags and quality counts from operational headlines; occupancy/RevPAR/profit and unique-guest claims remain excluded |
| Retail | How much value remains after credits, how concentrated is it, and how often do customers purchase again with comparable follow-up? | Gross/credits/net; 90-day repeat with eligible counts; customer concentration including credits; cohort and invoice-size context | Arbitrary RFM and duplicate quartile segments; reference intervals; unequal-follow-up repeat as the headline |
| Olist | What is commercial order value and volume, and how reliably are orders fulfilled? | Six executive KPIs: commercial GMV, commercial orders, AOV, cancellation rate, on-time rate, review score | Eleven competing executive measures reduced to six; purchase frequency, cohort, product/seller and freight detail moved to the relevant diagnostic pages; RFM predictions and NPS-like review labels removed |
| NYC | How much pickup activity is recorded, when/where does it occur, and what journey mix do the records describe? | Recorded pickups and per-day context; eligible mean duration/distance; calendar-normalized timing and zone/route rankings | Mixed-route speed removed as a performance headline; unproven duplicate-trip counts removed from the dashboard; quality/coverage kept as supporting evidence |
| Spotify | What was listened to, when, and how did the observed listening mix change? | Listening time/events, qualified plays, active dates, recording-aware rankings, repeat-event share and completion with coverage | Unavailable components shown as unknown; synthetic accuracy/recommendation/scoring claims removed; demo totals derived from one coherent fictional event fixture |

## Why this is useful

- **Cancellation rates need booking counts:** a high rate in a tiny segment cannot justify the same attention as a large cancellation volume. The Hotel comparisons retain that context.
- **ADR needs stay weighting:** a one-night and a ten-night booking should not contribute equally to a nightly-rate measure. The room-value proxy and rate use the same eligible population.
- **Repeat rates need equal follow-up:** Retail's headline is now **1,607 / 3,370 = 47.69% within 90 days**, with quarterly detail. The previous all-history rate mixed customers with different observation time.
- **Gross concentration needs credits/net:** a large buyer can generate large credits too. The same customer groups are compared on all three amounts instead of being assigned arbitrary loyalty labels.
- **Commercial value needs commercial denominators:** Olist's GMV, commercial orders and AOV align, while cancellations use the appropriate all-order population. Delivery rates use evaluable delivery records.
- **Timing comparisons need exposure:** NYC's monthly and weekday/hour views account for different numbers of calendar days. Recorded trips are not unmet demand or a complete transport census.
- **A recommendation score needs evidence:** Spotify's real model remains a documented heuristic, not a calibrated probability. Its synthetic edition now demonstrates consistent analytics and leaves model-dependent results unavailable.

Supporting details:

- [Hotel metric purpose and limits](HotelBookingAnalytics/docs/kpi_usefulness.md)
- [Retail metric purpose and limits](OnlineRetailPython/docs/kpi_usefulness.md)
- [Olist metric purpose and limits](OlistEcommerce/docs/kpi_usefulness.md)
- [NYC metric definitions and purpose](NYCTaxiAnalytics/docs/model.md)
- [Spotify metric purpose and limits](SpotifyTastePrototype/docs/kpi_usefulness.md)
- [Calculation tests and verification status](CALCULATION_REVIEW.md)
