# SQL model and derived features

## Schemas

- `stg`: nine source-shaped tables populated by BCP after Python validation.
- `core`: typed relational dimensions/facts with surrogate keys and enforced referential integrity.
- `rpt`: reusable analytical views for orders, customer/RFM, cohort retention, products, delivery, sellers, and geography.
- `etl`: batch/file audit, rejected-record metadata, and SQL data-quality results.

## Core tables

| Table | Grain | Notable fields |
|---|---|---|
| `core.Customer` | One real customer (`customer_unique_id`) | first/last purchase, cohort, lifetime purchase count/frequency, average days between purchases, new/repeat type |
| `core.Order` | One order | preserved `customer_id`, status/timestamps, purchase sequence, days since prior purchase, cohort, sales/freight/order value, delivery and promise performance |
| `core.OrderItem` | One item sequence per order | product, seller, price, freight, gross value, freight ratio |
| `core.Product` | One product | translated category and physical/catalogue attributes |
| `core.Seller` | One seller | seller geography |
| `core.Payment` | One payment sequence per order | type, installments, value |
| `core.Review` | One review/order pair | score, promoter/neutral/detractor group, optional comment and timestamps |
| `core.Geography` | One ZIP-code prefix | representative city/state, averaged coordinates, source point count |
| `core.Date` | One calendar date | standard calendar labels and sort columns |

## Customer identity

Olist's `customer_id` is unique to an order, so using it as a customer dimension would make every customer appear new. `core.Customer` instead keys on `customer_unique_id`; each order retains `OlistCustomerId` for lineage and links to the real customer surrogate `CustomerKey`.

## Geography handling

The geolocation source contains repeated coordinate observations. It is not treated as 261,831 “bad duplicates.” The model chooses the most frequently observed city/state label per ZIP prefix and averages valid latitude/longitude points. Customer and seller ZIP prefixes absent from geolocation are retained with null coordinates and surfaced by data-quality checks.

