# KPI definitions

| KPI | Definition |
|---|---|
| Sales / GMV | Sum of item price for orders not marked `canceled` or `unavailable`. Freight is excluded. |
| Orders | Distinct orders in the current filter context, regardless of status. |
| Order Status Share % | Orders for the visible status divided by all statuses while preserving every other active filter. |
| Sales Orders | Orders not marked `canceled` or `unavailable`. |
| Customers | Distinct `customer_unique_id` represented by the filtered orders. |
| Average Order Value | Sales / GMV divided by Sales Orders. |
| Items per Order | Commercial order-item rows divided by Sales Orders. Olist item quantity is represented by one row per item sequence. |
| Repeat Customer | A real customer (`customer_unique_id`) associated with more than one order in the full observed history. |
| Repeat Customer Rate | Customers whose visible orders are their second-or-later lifetime purchase divided by customers in the current context. Identity is based on `customer_unique_id`, not order-specific `customer_id`. |
| Customer Purchase Frequency | Sales Orders divided by Customers in the current filter context. |
| Delivered Orders | Orders whose final source status is `delivered`. |
| Cancelled Orders | Orders whose final source status is `canceled` (the source uses one L). |
| Delivery Rate | Delivered Orders divided by all Orders. |
| Cancellation Rate | Cancelled Orders divided by all Orders. |
| Average Delivery Days | Average elapsed days from purchase timestamp to customer delivery timestamp for delivered orders. |
| On-Time Delivery % | Delivered orders received on/before the estimated delivery date divided by delivered orders with both actual and promised dates. |
| Average Delay Days | Average positive actual-minus-promised days among late deliveries only. |
| Average Promise Variance Days | Signed average of actual delivery date minus promised date for delivered orders. Negative values indicate early delivery. |
| Freight Cost | Sum of order-item freight for commercial orders. |
| Freight Ratio | Freight Cost divided by Sales / GMV. It is a ratio of aggregates, not an average of row ratios. |
| Average Review Score | Mean valid review score (1–5). Multiple reviews per order remain distinct. |
| Product Orders / Seller Orders | Distinct commercial orders represented by the visible product or seller item rows; canceled and unavailable orders are excluded to align with GMV. |
| Retention Rate | Customers active in cohort month N divided by the original Month-0 cohort size. Only non-canceled/non-unavailable orders count as activity. Matrix grand totals are intentionally blank because rates across different cohorts and periods are not additive. |

Currency is Brazilian real (R$). The dataset does not include currency conversion, refunds, cost of goods, or profit; the report therefore avoids profit terminology.
