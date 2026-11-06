# Calculation validation

Source calculations were validated on 2026-11-04. The current report subsequently passed native Power BI Desktop refresh on 2026-11-06 after correcting the Date import's numeric-to-logical conversion. All six pages, a São Paulo cross-filter and customer drill-through were checked and saved; [native evidence](qa/powerbi_native_validation_2026-11-06.md) records the exact scope and current screenshots.

| Check | Result |
|---|---|
| `tests/assert_model.sql` against `Portfolio_Olist` | PASS: 96,096 customers, 99,441 orders, 112,650 order items, zero rejected records in the latest load batch |
| `tests/verify_exports.py` independent source/export checks | PASS: 99,441 orders, 98,207 commercial orders, 94,990 commercial customers, 2,888 repeat commercial customers, 254 cohort cells |
| DAX/report source audit | 47 measures and 54 visuals; executive page reduced to six measures with explicit decision purposes |
| Findings recomputation | Refreshed exports independently aggregated for GMV, order denominators, repeat customers, eligible deliveries, review weighting, state rates, cohorts, seller contribution and freight |

The SQL assertions check source-to-core counts, unique business keys, factual completed-purchase groups, commercial-only purchase sequences, cohort Month 0, cohort bounds and numerators, and seller on-time rates against an independent distinct-order reference. The assertions materialize commercial sequences once to avoid repeated window-view scans on SQL Express; this changes test execution, not the reference logic. The exact successful SQL output is in [sql_assertions_result.txt](sql_assertions_result.txt). Null-aggregate warnings are expected because ineligible delivery observations are intentionally excluded from averages.

The source/export checks additionally reconcile the corrected commercial purchase/cohort definitions and reporting calculations. A full report render or source-layout check cannot substitute for executing DAX in Power BI Desktop.

Important population checks:

- On-time delivery uses 96,470 evaluable delivered orders, including 89,936 on-time orders. Eight delivered orders without usable timestamps are excluded.
- The commercial order count is 98,207. Eight commercial orders lack item records, so item-scoped populations can contain 98,199 orders. GMV/AOV documentation keeps that distinction explicit.
- Seller and product experience calculations use distinct orders to avoid giving multi-item purchases extra weight. Reviews remain distinct source review records.
- Repeat customers and cohorts start with commercial purchases. Forty-four customers had a canceled/unavailable order before their first commercial purchase; those earlier orders do not advance purchase sequence or cohort assignment.
- Cohort data includes eligible zero-activity cells and omits partial boundary months. Month-1 retention is 428/88,779 = 0.48% across cohorts with a full Month 1.

See [KPI usefulness](kpi_usefulness.md) for the purpose and limitations of the six headline measures, and [findings](findings.md) for verified results. The reviewed source and current previews are included in the published portfolio.
