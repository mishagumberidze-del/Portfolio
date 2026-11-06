# Power BI Desktop validation — 2026-11-06

The current Olist PBIP was opened, refreshed and saved in Power BI Desktop 2.159.1231.0. All six report pages rendered successfully after refresh. This supersedes the earlier runtime handoff.

## Refresh correction

The Date export represents `IsWeekend` as numeric text (`0`/`1`). Direct conversion to Power Query logical values produced 800 Date-row errors. The import now converts the field to `Int64.Type`, then maps `1` to true. The corrected native refresh completed and the incomplete-data and relationship-refresh warnings cleared. A private recovery copy of the saved project was retained outside the published source package.

## Observed results

- Executive overview: GMV **R$13.49M**, commercial orders **98K**, AOV **R$137.41**, cancellation **0.63%**, on-time delivery **93.23%**, review score **4.09**. These rounded displays match the independent source totals of 13,494,400.74 GMV and 98,207 commercial orders.
- Selecting São Paulo in the state chart changed GMV to **R$5.16M**, orders to **41K**, AOV to **R$125.56**, cancellation to **0.78%**, on-time delivery to **95.51%** and review score to **4.17**. Clearing the selection restored the overall values.
- Customer Deep Dive showed **3.04%** repeat commercial customers and **1.03** commercial purchases per purchaser. The cohort matrix rendered with complete-month data.
- Product, delivery and seller pages rendered their charts and tables. Seller rankings completed rendering without visual errors.
- Right-clicking a customer in the customer table and selecting **Drill through → Customer Detail** displayed one customer and one order, **R$13.44K** commercial GMV/AOV, review score **1.00** and an on-time delivery. Executive Overview was restored before saving.

These checks validate the refresh, six rendered pages, one state cross-filter and one customer drill-through. They do not claim exhaustive testing of every possible filter combination. SQL assertions and independent source reconciliation are recorded in [the calculation QA](../qa.md).

## Current screenshots

- [Executive overview](reviewed-executive-2026-11-06.png)
- [São Paulo filter](reviewed-sp-filter-2026-11-06.png)
- [Customer Deep Dive](reviewed-customer-2026-11-06.png)
- [Product & Sales](reviewed-product-2026-11-06.png)
- [Delivery & Experience](reviewed-delivery-2026-11-06.png)
- [Seller & Geography](reviewed-seller-2026-11-06.png)
- [Customer Detail after drill-through](reviewed-detail-2026-11-06.png)
