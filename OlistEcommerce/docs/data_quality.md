# Data quality results

Load batches 1 and 2 completed successfully on 2026-09-09 UTC. Batch 2 was the explicit idempotency rerun and reproduced the same core counts with no duplicates.

- 1,550,922 rows were read and staged from nine files.
- Python schema/type/key checks: zero failed error checks.
- SQL rejected records: zero.
- Core order and customer grain reconciliation: passed.
- Duplicate order and order-item business keys: none.
- Negative price/freight values: none.

Retained warnings:

| Warning | Rows | Treatment |
|---|---:|---|
| Delivered status but missing customer-delivery timestamp | 8 | Kept as delivered; delivery duration and on-time classification remain unavailable. |
| ZIP prefixes without coordinates | 162 | City/state retained; map coordinate remains null. |
| Payment differs from item price + freight by more than R$0.05 | 1,032 | Both values retained at their correct grains; no forced reconciliation. Differences can reflect vouchers, split payments, or source anomalies. |
| Products with unknown English category | 623 | Retained and grouped as `Unknown`; includes 610 source-null Portuguese categories plus untranslated values. |

The 261,831 fully duplicated geolocation observations are not silently loaded as separate locations. Geolocation is intentionally aggregated to one ZIP-prefix record using averaged valid coordinates and the most frequent city/state label.

Model reconciliation note: 775 orders have no source order-item row. Of these, 767 are `canceled` or `unavailable`; the remaining eight are five `created`, two `invoiced`, and one `shipped` order. They remain in order/status KPIs and in the 98,207 commercial-order denominator, but cannot appear in product or seller item context, which therefore contains 98,199 distinct commercial orders. This grain difference is intentional and documented rather than silently forcing the counts to match.
