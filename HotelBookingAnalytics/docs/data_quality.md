# Source profile and cleaning decisions

Source SHA-256: `7c2ae42a7353905ea136e5c2287f17c92c5435826598bfbb8491c6f0c7b1fc06`.

| Observation | Rows | Treatment |
|---|---:|---|
| Raw and core bookings | 119,390 | All preserved; source row number is unique trace key |
| Exact duplicate source records | 31,994 | Preserved because no booking ID exists |
| Missing country | 488 | SQL NULL |
| Missing children | 4 | SQL NULL; zero-guest flag uses zero only for its check |
| Missing agent | 16,340 | SQL NULL |
| Missing company | 112,593 | SQL NULL |
| Zero-night bookings | 715 | Preserved and flagged |
| Negative ADR | 1 | Preserved and flagged |
| Zero-guest bookings | 180 | Preserved and flagged |
| Bookings with any of these three quality flags | 826 | Flagged in `core.Booking` |

Source `NA` and `NULL` placeholders in the four nullable fields are normalized to SQL NULL. Numeric values and source schema are checked before staging. The raw CSV is never modified.
