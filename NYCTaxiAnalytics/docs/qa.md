# Calculation audit — 2026-11-04

- `tests/verify_source_metrics.py` independently scanned all 298,930,712 original rows and verified all 48 source hashes, time metric numerators and denominators, zones, bands, quality flags and 65 complete top-route rankings against the dashboard exports.
- Calendar exposure and service-capability denominator regression tests passed (`node --test tests/metrics.test.cjs`). `tests/verify_data.py` passed, including deterministic generation of the updated HTML.
- The revised browser harness passed all 65 service/month states at 1440 px and 390 px, including weighted means, calendar exposure, quality denominators, keyboard map selection, reset and CSV download, with no page overflow. Current captures are `site/qa/reviewed-desktop.jpg` and `site/qa/reviewed-mobile.jpg`; results are in `site/qa/browser-results-2026-11-04.json`. Earlier captures below remain historical.

## Earlier-build QA

# QA — 2026-11-02

- Original acquisition: 48 monthly Parquet files fully decoded in the existing source project, plus eight reference downloads; source inspection and manifests are retained in `reports/tlc_2024/`.
- This pipeline checked all source SHA-256 hashes against the acquisition manifest and processed every one of the 298,930,712 source rows in bounded batches. It derived actual timestamp features rather than assuming file partitions are pickup months.
- 298,930,636 in-year records reconcile exactly across source-file, time, pickup-zone, dropoff-zone and route counts. The remaining 76 records have pickup timestamps outside 2024 and remain visible in the source quality audit.
- A cached rerun rechecked hashes and reused all 48 completed file profiles. Two complete SQL aggregate loads produced identical source/core counts and aggregate cell counts. The database has 136 MB allocated to data files at verification time; transaction log and external files are additional storage.
- Core counts: 8,268 time cells, 25,222 zone-role cells, 1,513,268 route cells. Reporting views consolidate source-file contributions into 8,064 time cells and 24,456 zone-role cells.
- `tests/verify_data.py` checks per-service/month denominators, distribution totals, pickup/dropoff reconciliation, all top-route states, FHV unavailable metrics and deterministic HTML generation.
- The 263 official SHP polygon records were paired with their DBF LocationIDs and checked for unique IDs. Boundaries were reviewed visually with the NYC projected layout and correct north-up orientation.
- Browser checks passed all 65 service/month states at desktop and narrow widths. Tests compared displayed counts to independently prepared SQL-export expectations; verified 263 map features, ten route ranks per state, map keyboard selection, month-chart selection, FHV distance availability, reset and a nonempty filtered aggregate CSV download.
- Layout checks passed at desktop viewport 1,422 px and narrow viewport 504 px, with no page-level horizontal overflow. Captures are in `site/qa/desktop.png` and `site/qa/narrow.png`. The desktop capture covers all dashboard sections; the narrow capture covers the top sections and map.
- The self-contained index has no external data/library/font/map-tile dependencies. Its only external link is the official TLC documentation link. The local HTTP preview returned 200; the Codex preview-open request was queued by the app, so it is not counted as independent visual verification.

## Repeat checks

```powershell
python .\tests\verify_data.py
python .\tests\prepare_browser_qa.py
```

The second command creates a Git-ignored test page under `data/processed/dashboard/`. Load that page in a local browser or headless renderer, wait for the asynchronous CSV test, and inspect the document element's `data-qa-result` attribute. Successful results report `status: pass`, 65 combinations and no layout overflow. It does not contact external services.
