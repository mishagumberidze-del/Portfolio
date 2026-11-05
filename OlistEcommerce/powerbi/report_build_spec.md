# Power BI report specification

This document describes the implemented report. The reproducible definition source is `build_report.mjs`.

## Global design

- Canvas: 16:9 at 1920×1080.
- Theme: navy headers, teal primary marks, gold comparisons, muted red only for adverse outcomes.
- Consistent 24 px margins, 16 px visual gaps, eight-pixel card padding, Segoe UI typography, and restrained borders.
- Five main analytical pages and one customer drill-through page.
- All analytical logic comes from the semantic model and SQL-produced extracts, not raw CSVs.

## 1. Executive Overview

- Slicers: year, customer state, order status, dynamic trend metric.
- Three commercial KPIs: Sales / GMV, Sales Orders, and Average Order Value, using the same commercial population.
- Three fulfilment and experience KPIs: Cancellation Rate, On-Time Delivery %, and Average Review Score, with their documented eligible populations.
- Dynamic monthly trend, readable order-status count/share table, and customer-state sales ranking.

## 2. Customer Deep Dive

- Slicers: customer type, completed-purchase group, home state.
- Commercial customer count, repeat customers/rate, purchase frequency, and GMV per commercial customer; first-versus-repeat commercial GMV trend.
- GMV by factual completed-purchase count group (0, 1, 2, 3+ delivered purchases). These groups do not predict loyalty or churn.
- Cohort retention matrix by first commercial purchase month and months since that purchase. Customer filters apply, partial boundary months are excluded, and non-additive grand totals are suppressed.
- Customer value table supporting right-click drill-through.

## 3. Product & Sales

- Slicers: translated product category and year.
- GMV, units, product orders, ASP, and customers-in-item-context KPIs.
- Category contribution and unit-demand rankings.
- Top 25 products by commercial item sales with product, customer, and review context metrics.

## 4. Delivery & Customer Experience

- Slicers: delivery outcome, order status, review group.
- Delivery, late-delay, freight, and review KPIs.
- Promise outcome distribution, freight burden by customer state, and monthly delivery trend.
- Delivery timing by review group uses explicit DAX context bridges rather than bidirectional relationships.
- Review score-group mix.

## 5. Seller & Geographic Performance

- Slicers: seller state and dynamic Top N.
- Seller network KPIs.
- Dynamic seller sales ranking, seller-state sales, and detailed fulfilment/customer/review table.

## Customer Detail drill-through

- Bound to the unique presentation label `Customer[Customer Display ID]`; the full source `customer_unique_id` remains preserved as a hidden traceability field.
- Customer summary and profile, single-unit sales history, delivery-outcome mix, and order-level detail.

## Validation checklist

- Month 0 cohort cells equal 100%; later months retain original cohort denominators.
- Customer identity is one row per `customer_unique_id`; order-specific `customer_id` remains on Order.
- Product and seller measures traverse Order Item and use explicit context-bridge measures where Order/Review metrics are needed.
- Top-N controls suppress nonselected sellers and the product detail is capped at 25 rows.
- Product/seller order and customer context measures use the same non-canceled/non-unavailable commercial scope as GMV.
- Currency and count series are not plotted on the same axis; skewed outcome distributions retain readable category values.
- SQL assertions, source/export reconciliation, and generated PBIR/TMDL source checks passed on 2026-11-04; see `../docs/qa.md` for the evidence and scope.
- Power BI Desktop refresh, rendered visuals, and interactive DAX/filter behavior remain to be validated in the native application. Historical screenshots do not validate the rebuilt definitions.
