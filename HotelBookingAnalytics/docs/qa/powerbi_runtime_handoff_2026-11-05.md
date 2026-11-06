# Earlier Power BI runtime handoff — 2026-11-05

**Resolved later on 2026-11-05:** the corrected report was refreshed, all three pages reviewed and saved, and the City Hotel filter matched the checked weighted ADR. See [native validation and current screenshots](powerbi_native_validation_2026-11-05.md). The remaining text preserves the earlier attempt and its limits.

Power BI Desktop 2.159.1231.0 opened successfully during this pass. Opening the current Hotel PBIP exposed this native model error:

> A circular dependency was detected: Booking[LeadTimeBand], Booking[LeadTimeBandSort], Booking[LeadTimeBand].

Fixed `LeadTimeBandSort` to depend on numeric `LeadTime` and `StayBandSort` to depend on numeric `StayNights`, in both `powerbi/build_pbip.py` and the checked-in `Booking.tmdl`. Neither sort expression now depends on the label it sorts. An independent check matched the numeric sort keys against every exported label across all 119,390 bookings. The isolated project rebuild also passed.

Post-fix native validation remains incomplete. The final window capture returned “foreground window did not report a process id”; window discovery then returned no Power BI window. No post-fix load, refresh, DAX evaluation or displayed KPI/filter result is claimed. No new report screenshots were saved.

To finish manually, open `powerbi/HotelBookingPBIP/HotelBooking.pbip`, refresh, and check all three report pages. With filters cleared, expect 119,390 bookings, 37.04% cancellation, 25,996,324.21 CU room-value proxy and 101.93 CU stay-weighted ADR. City Hotel should show 79,330 bookings and 106.50 CU weighted ADR; Resort Hotel should show 40,060 bookings and 96.78 CU weighted ADR. Existing screenshots predate this fix.
