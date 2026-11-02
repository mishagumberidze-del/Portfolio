# Source profile and cleaning decisions

Source SHA-256: `280b35708524f2558b947d16986c858b3359a97e99df5f9a4c22f4e18ee65a78`.

| Observation | Rows | Treatment |
|---|---:|---|
| Raw and core lines | 541,910 | All preserved |
| Exact duplicate source rows | 5,268 | Preserved; no line ID exists |
| Missing customer ID | 135,080 | SQL NULL; excluded from customer/cohort metrics |
| Missing description | 1,454 | SQL NULL; product label selected from most frequent nonblank description |
| `C`-prefixed invoice lines | 9,288 | Flagged on invoice |
| Negative quantity lines | 10,624 | Treated as returns or adjustments when price is positive |
| Negative quantity without `C` prefix | 1,336 | Still classified by quantity; flagged in source profile |
| Zero price | 2,515 | Retained, excluded from value metrics |
| Negative price | 2 | Retained, excluded from value metrics |
| Invoices with multiple timestamps | 43 | Earliest timestamp on header; original timestamp on each line |

The CSV is Latin-1 encoded. The pipeline validates schema, timestamps, numeric values, empty keys, and tab/newline safety before export to staging. Original bytes are untouched.
