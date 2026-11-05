# Verification — calculation and KPI review

The curated release incorporates the calculation and usefulness review across all five projects. [KPI_REVIEW.md](KPI_REVIEW.md) explains why the retained measures matter; [CALCULATION_REVIEW.md](CALCULATION_REVIEW.md) records corrected definitions, source reconciliation and remaining validation limits.

## Current checks

- Hotel and Retail metrics independently reconcile to their original source files and SQL exports. Generated report artifacts match isolated rebuilds.
- NYC checks cover all 298,930,712 source records, all 48 source hashes, aggregate metrics and route rankings. Both calendar-exposure and field-eligibility JavaScript tests also pass in this curated copy. Current browser checks passed all 65 states at 1,440 px desktop and 390 px mobile, including metrics, calendar exposure, quality denominators, downloads and overflow. [Results](NYCTaxiAnalytics/site/qa/browser-results-2026-11-04.json) and current screenshots are included.
- Olist source/export reconciliation passed for 99,441 orders, commercial customer identity, 254 cohort cells, hashes and report fields. Final SQL assertions passed. The model contains 47 measures and 54 visuals with six executive KPIs.
- Spotify's reviewed source passed 149 backend tests, TypeScript compilation and a production demo build. All 23 frontend tests also pass in this curated copy. The synthetic edition derives its analytics from one fictional event fixture and leaves fitted model results, recommendation scoring and human evaluation unavailable.
- The curated release audit checks excluded data/secrets, machine paths, JSON and relative documentation links. The static site check passes 13 local asset/link references and GitHub Pages relative-path requirements; portfolio JavaScript syntax passes.

## Visual and deployment limits

NYC and Retail screenshots show the current reviewed reports. Retail passed desktop/mobile checks for cohort shading, chart readability and page overflow; [results](OnlineRetailPython/report/qa/browser-results-2026-11-05.json) are included. Spotify’s current fictional overview, last-30-day range and unavailable scorer passed desktop/mobile checks with no page overflow; [results](SpotifyTastePrototype/docs/qa/browser-results-2026-11-05.json) and current overview screenshots are included. Hotel and Olist screenshots remain **pre-audit visual previews** until replaced by a current render. Earlier Power BI Desktop checks remain historical evidence; current static model/export/reference checks are recorded for [Hotel](HotelBookingAnalytics/docs/qa/powerbi_validation_2026-11-04.md) and [Olist](OlistEcommerce/docs/qa/powerbi_validation_2026-11-04.md). Native Power BI refresh, DAX execution and filter behavior remain unverified. The Hotel circular sort dependency was fixed in the builder and generated model; native post-fix verification is not claimed. Current handoff records are available for [Hotel](HotelBookingAnalytics/docs/qa/powerbi_runtime_handoff_2026-11-05.md) and [Olist](OlistEcommerce/docs/qa/powerbi_runtime_handoff_2026-11-05.md). The completed source/data tests do not replace those native checks.

The portfolio release updates the static site and reviewed source packages. The separately hosted Spotify read-only edition is independently managed; its private snapshot is excluded from this repository.

## Included and excluded

Included: five project source packages, Power BI source definitions, standalone aggregate Taxi/Retail HTML reports, metric documentation, synthetic Spotify fixture and automated tests.

Excluded: raw datasets and customer-level exports, personal listening snapshots, credentials/tokens, local connection overrides, database files, dependency folders, runtime caches and internal session notes. Power BI requires the documented data pipelines before refresh. The checks above do not imply a clean-machine end-to-end build.
