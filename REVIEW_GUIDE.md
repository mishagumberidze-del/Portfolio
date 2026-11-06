# A quick review guide

## SQL / BI review

1. Start with [Olist's model and derivations](OlistEcommerce/docs/data_model.md), then inspect [core transformations](OlistEcommerce/sql/03_transform_core.sql) and [reporting views](OlistEcommerce/sql/04_reporting_views.sql).
2. Inspect the [Power BI measures](OlistEcommerce/powerbi/measures.dax) and the PBIP semantic model. The implementation distinguishes order-specific customer IDs from enduring customer identity.
3. Compare [Hotel's source reconciliation checks](HotelBookingAnalytics/sql/03_validate.sql) with [its metric boundaries](HotelBookingAnalytics/README.md). Room-value proxies are labelled explicitly.

## Python / scale / statistics review

1. Read the [Taxi streaming pipeline](NYCTaxiAnalytics/etl/prepare_analytics.py) and [aggregate model](NYCTaxiAnalytics/docs/model.md). It processes all 48 source files and retains file lineage while fitting the local SQL Server Express deployment.
2. Inspect [SQL route ranking](NYCTaxiAnalytics/etl/export_dashboard.ps1) and [browser interaction tests](NYCTaxiAnalytics/tests/browser_qa.js).
3. Read the [Retail report generator](OnlineRetailPython/report/build_report.py) for customer concentration, purchase cohorts, a fixed 90-day repeat window and distribution checks. [Metric definitions](OnlineRetailPython/docs/model.md) explain the population and interpretation limits.

## Application review

1. Explore Spotify's [domain](SpotifyTastePrototype/backend/SpotifyTaste.Domain), [application layer](SpotifyTastePrototype/backend/SpotifyTaste.Application), [API](SpotifyTastePrototype/backend/SpotifyTaste.Api) and [tests](SpotifyTastePrototype/backend/SpotifyTaste.Tests).
2. Inspect the [React source](SpotifyTastePrototype/frontend/spotify-taste-web/src), including the music story, discovery lab, number formatting and API adapters.
3. Use synthetic demo mode to review the interface without personal data or external account access. See the [Spotify README](SpotifyTastePrototype/README.md).

## Starter-pack boundaries

- Reports, source, current Hotel/Olist/NYC/Retail/Spotify demo screenshots are included. Older screenshots do not verify the audited calculations. Large raw data, SQL database files and Power BI cache/PBIX files are not.
- Analytical aggregates in the Taxi/Retail HTML artifacts derive from public datasets. Spotify private snapshots are not included.
- Existing project QA notes describe local verification; they do not imply that a hosted service or a fresh machine has been tested automatically.
- Third-party packages and datasets retain their own terms. Dataset references are listed separately.
