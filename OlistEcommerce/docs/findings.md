# Evidence-based findings

All figures come from `Portfolio_Olist`; queries are in `sql/06_portfolio_analysis.sql`.

- The observed history contains 99,441 orders from 96,096 real customers and R$13.49M in commercial merchandise GMV. Average order value is R$137.41 and commercial orders contain 1.141 item rows on average.
- Only 2,997 customers placed more than one observed order, a lifetime repeat-customer rate of 3.12%. Repeat buyers averaged 80.08 days between purchases. The dataset therefore behaves much more like an acquisition-led marketplace snapshot than a high-frequency retail base.
- Delivery performance is strong on incidence but consequential when it fails: 93.23% of delivered orders with usable promise dates arrived on time. On-time deliveries averaged a 4.29 review score, compared with 2.27 for late deliveries. This is association, not proof of causation.
- Late delivered orders took 33.95 days on average versus 11.00 days for on-time orders.
- São Paulo generated R$5.16M, about 38.3% of total commercial GMV. Rio de Janeiro was second at R$1.81M but had a materially lower average on-time rate (86.5%) than São Paulo (95.4%).
- Health & beauty was the leading category at R$1.26M, followed by watches & gifts at R$1.20M and bed, bath & table at R$1.04M.
- November 2017 was the highest-GMV month at R$1.00M. The dataset ends in October 2018 and the edge months are partial, so they should not be used for naive month-over-month conclusions.
- Weighted Month-1 cohort retention is 0.46%; subsequent observed monthly retention is around 0.2–0.3%. Cohort cells correctly include zero-return cohorts and only compare cohorts old enough to reach each month.
- The top ten sellers account for 13.2% of commercial GMV, indicating that sales are not dominated by a tiny seller group.
- Freight equals 16.61% of merchandise GMV at the aggregate level.

