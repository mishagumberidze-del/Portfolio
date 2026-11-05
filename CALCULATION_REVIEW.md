# Calculation and usefulness review — 2026-11-05

The calculation and [KPI logic review](KPI_REVIEW.md) is complete across all five projects. Corrections are saved in the working folders and synced to the curated local `.publish/Portfolio` checkout. Source/calculation checks and application tests passed as recorded below. NYC and Retail were checked in a browser at desktop and phone widths; Spotify's rebuilt demo overview, range filtering and unavailable scorer were also checked. Power BI post-fix refresh and interactive DAX/filter verification remain unverified. The reviewed release is prepared for publication; runtime limitations are recorded below.

## Applied changes

| Project | Correction and purpose | Verification |
|---|---|---|
| Hotel | Replaced booking-average ADR with a stay-weighted rate. Rate and room-value proxy now share an eligible population. Operational metrics use non-canceled positive-night stays. Labels distinguish room-assignment differences from actual moves. Operational headlines now use stay count/nights instead of repeat flags or quality counts. | Independent checks passed for all 119,390 source/export bookings and 39 hotel/year/segment groups. Reporting views were updated without reloading core data. PBIP rebuilt deterministically. |
| Retail | Replaced arbitrary RFM/quartile labels and reference intervals with customer concentration including gross, credits and net, and observed 90-day repeat rates with eligible/repeated counts. Removed a redundant product chart. The repeat headline now uses a pooled 90-day window (1,607 / 3,370 = 47.69%). | Independent checks passed for all 541,910 raw/export lines, values, customer frequency, concentration, invoice distributions and 90-day denominators. HTML and summary match an isolated rebuild. |
| NYC | Monthly and weekday/hour comparisons now account for calendar exposure. Distance/fare quality percentages exclude services that do not report those fields. Removed mixed-route speed as a performance headline and the unproven duplicate-trip indicator; clarified screening rules and mapped zones. | All 298,930,712 original rows and 48 hashes independently verified against exported metric numerators/denominators, zones, distributions, quality counts and 65 route rankings. Two focused JS tests and deterministic dashboard checks passed. |
| Olist | Aligned cohorts and purchase sequences to commercial orders; corrected distinct-order weighting, seller/product rankings, delivery denominators and customer-filter-aware retention. Replaced arbitrary RFM labels with factual completed-purchase groups and removed incomplete-period comparisons. | Final live SQL assertions passed: 96,096 customers, 99,441 orders, 112,650 items and zero latest-batch rejected rows. Independent original-source/export checks passed for 99,441 orders, 254 cohort cells, export hashes, 47 measures and 54 visuals. Desktop DAX/filter validation remains unverified. |
| Spotify | Made calendar-month context explicit for rolling ranges; suppressed unavailable score components; tightened returning-favorite and equivalent-recording logic. Added benchmark handling for tied evidence and absent comparable neighbors. Rebuilt the synthetic demo around consistent fictional events and removed fabricated model results. | 149 backend tests, 23 frontend tests, TypeScript compilation and a production demo build passed. |

## Useful retained metrics

- Hotel: booking/cancellation volume, eligible stay nights, stay-weighted ADR, room-value proxy, stayed bookings/stay-nights and room-assignment differences.
- Retail: gross/credit/net exposure, customer concentration, identified-customer coverage, fixed-90-day repeat purchasing, comparable follow-up cohorts and invoice-value distribution.
- NYC: recorded pickup volume, calendar-normalized patterns, eligible trip duration/distance, mapped zones/routes and field-specific coverage.
- Olist: commercial GMV and orders, customer purchase frequency, delivery/review experience, commercially consistent cohorts and seller/product contribution.
- Spotify: actual listening time and events, explicit time coverage, measured available preference signals, and clearly heuristic recommendation scores.

## Confirmed values

- Hotel: 74,486 non-canceled positive-night stays; 255,030 rate-eligible nights; room-value proxy **25,996,324.21 CU**; stay-weighted ADR **101.93 CU**. The old unweighted ADR was 100.90.
- Retail: gross **10,666,702.54 CU**, credits **896,812.49 CU**, net **9,769,890.05 CU**. Top decile: 434 of 4,338 identified purchasers. Fixed 90-day repeated/eligible counts: 527/885, 560/1,249, 329/826 and 191/410 for 2010Q4 through 2011Q3.
- NYC: **298,930,636** pickups within 2024; **76** source pickups outside the year remain in quality audits.
- Olist refreshed customer extract: **94,990** commercial purchasers, **2,888** with repeat commercial purchases (**3.04%**). State delivery comparisons confirmed São Paulo **95.51%** and Rio de Janeiro **87.89%**, using eligible order weighting.

## Final verification and release status

- **NYC:** the current browser harness passed all 65 service/month states at 1440 px and 390 px, including weighted metrics, exposure, eligible quality denominators, map interaction, reset and CSV download. Current desktop/mobile screenshots and [results](NYCTaxiAnalytics/site/qa/browser-results-2026-11-04.json) are saved.
- **Retail:** corrected a saturated heatmap color formula to a linear 0–100% scale, added a legend, and made small-screen charts readable in keyboard-scrollable regions. Final browser checks confirmed 30 distinct populated heat shades, the correct headline values and no page overflow at 1440 px or 390 px. Current [browser evidence](OnlineRetailPython/report/qa/browser-results-2026-11-05.json) and screenshots are saved. The isolated HTML/summary rebuild passed.
- **Spotify:** fixed named static builds so `build:demo` and `build:personal` select the correct data adapter without a local environment file. TypeScript, the production demo build and all 23 frontend tests passed. The demo overview rendered 6,368 lifetime events, changed to 116 for Last 30 days, and left scoring unavailable with an explanation. Desktop/phone layouts had no page overflow. [Browser evidence](SpotifyTastePrototype/docs/qa/browser-results-2026-11-05.json) covers those screens, not every application screen.
- **Hotel:** Desktop opening exposed a circular dependency between the two band labels and their sort columns. Sort keys now derive directly from the numeric lead time and stay nights; all 119,390 source labels reconcile and the isolated PBIP rebuild passed. A post-fix native reopen/refresh could not be verified. No new Hotel screenshots or native DAX success are claimed.
- **Olist:** final SQL assertions and source/export reconciliation passed; native Power BI refresh and interactive filters remain unverified. Its screenshots remain pre-audit previews. SQL output is retained in [the assertion result](OlistEcommerce/docs/sql_assertions_result.txt).
- The curated local release contains the reviewed source, report definitions, generated reports, tests, current web previews and corrected descriptions. This is the reviewed publication package; raw datasets, private snapshots and credentials remain excluded.

## Repeating completed checks

Use a Python environment with each project's requirements installed:

```powershell
python HotelBookingAnalytics/tests/verify_metrics.py
python OnlineRetailPython/tests/verify_metrics.py
python verify_rebuild.py
node --test NYCTaxiAnalytics/tests/metrics.test.cjs
python NYCTaxiAnalytics/tests/verify_data.py
python NYCTaxiAnalytics/tests/verify_source_metrics.py --raw-dir C:\path\to\tlc\raw
```

Spotify's frontend checks are `node --test tests/*.test.mjs` and `node node_modules/typescript/bin/tsc -b` from its frontend directory.

Earlier Power BI screenshots remain historical. The dated NYC, Retail and Spotify web captures above show the reviewed revision and only the verification scope stated here.
