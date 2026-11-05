# Analytical model and definitions

## Relational structure

- `core.Service`: four distinct published TLC populations; service ID is stable.
- `core.Month`: the twelve 2024 calendar months, used in actual pickup and source-file partition roles.
- `core.Zone`: the 265 official lookup IDs plus ID 0 for unmatched/null values. Lookup IDs 264 and 265 are not given invented polygons.
- `core.SourceFile`: 48 source file IDs, service/month foreign keys, original filenames, pinned hashes, source rows and in-year rows.
- `core.TimeProfile`: source file × actual pickup month × weekday × hour. Stores counts, eligible denominators and additive numerators; averages are recomputed correctly from sums.
- `core.ZoneProfile`: source file × actual pickup month × zone × pickup/dropoff role.
- `core.RouteProfile`: source file × actual pickup month × directed pickup/dropoff zone pair. Contains 1,513,268 aggregate cells, not individual trips.
- `core.BandProfile`: source file × actual pickup month × metric × duration/distance band.
- `core.Quality`: source file × overlapping check name. Source files are never deduplicated by projected trip attributes.
- `etl.LoadAudit`: source and in-year totals, aggregate cell counts, timestamp and final status after reconciliation.

Each fact has a composite primary key and foreign keys to its dimensions. `stg` mirrors the exported aggregate grains. The `rpt` views combine source-file contributions into dashboard grains. SQL computes top ten routes for each of the 65 service/month states using grouping sets and ranking; the browser does not guess overall rankings from truncated monthly lists.

The immutable Parquet files retain complete source fields and trip-level rows. SQL is an analytical aggregate model chosen for the local Express instance. A trip-level warehouse migration would be a separate storage/design task; no per-trip SQL lineage or trip-level SQL query capability is claimed here.

## Rules

- **Population:** actual pickup timestamp falls in `[2024-01-01, 2025-01-01)`. 76 out-of-year pickups are kept in source audits and excluded from year charts.
- **Calendar exposure:** monthly chart values are pickups divided by calendar days in each month. Heatmap cells are weekday/hour totals divided by occurrences of that weekday in the selected period. Leap-year 2024 has 366 days; zero-trip dates remain in the denominator. Tooltips retain the original totals.
- **Duration:** dropoff minus pickup; eligible when >0 seconds and <=14,400 seconds. No timezone or DST correction is inferred from naive local source clocks.
- **Distance:** source reported miles, >0 and <=200. Not available in FHV. FHV is not included in distance denominators.
- **Speed screening:** implied speeds above 100 mph among duration/distance-eligible pairs are quality flags. Legacy matched-distance/time aggregates remain in the analytical exports for audit, but speed is no longer a headline dashboard KPI: route and service mix prevent it from measuring congestion or operator performance. No straight-line distance is inferred.
- **Base fare:** source `fare_amount` for Yellow/Green and `base_passenger_fare` for high-volume FHV; eligible when nonnegative and <=$1,000. Their meanings differ, so only a selected service's average is shown. Missing FHV fare is not zero.
- **Zones:** null/unmatched source IDs become explicit aggregate zone 0; IDs 264/265 are preserved. Counts are not lost through joins to physical polygons.
- **Bands:** upper bounds are inclusive. Duration bands end at 10,20,40,60,240 minutes; distance bands end at 1,3,5,10,200 miles.
- **Projected duplicate candidates:** legacy pipeline audit counts are retained, but are no longer displayed as a dashboard metric: identical selected fields in one batch cannot establish duplicate trips. All candidates remain in every total.
- **Quality scope:** all source-file rows, including out-of-year rows. Flags overlap; they must not be summed as rejected trips. Dashboard filtering selects source partition month for this panel, while demand charts use actual pickup month.
- **Quality denominators:** duration and zone checks use all selected source rows. Distance, speed and fare checks use only selected rows from services reporting those fields, excluding FHV. Screening boundaries are analytical choices, not proof that records beyond the limits are erroneous.
- **Precision:** source numeric doubles are aggregated as floating-point, preserving their source scale. Dashboard SQL exports round additive continuous numerators to three decimal places; count reconciliations are exact. No financial-cent reconciliation is claimed for differing service populations.

## Geography

The official SHP/DBF records are paired in file order and joined by LocationID. Polygon boundaries retain the supplied NAD83 New York Long Island US-survey-foot coordinates; SVG paths use a uniform projected scale with north up. Boundary simplification tolerance is 120 source feet (about 37 metres) for display only. No SRID relabeling or fabricated longitude/latitude is used. Holes and multipart boundaries use SVG even-odd filling. The original archive remains unchanged.

## Why these headline measures are retained

Recorded pickups establish the scale of the selected submission population; the per-calendar-day note supports period comparisons. Eligible mean duration and reported distance describe the journey mix. Service/month filters, calendar-normalized timing and zone/route rankings locate patterns to investigate. These are descriptive metrics, without invented targets or claims of unmet demand, transport market share, congestion reduction or operational impact. Coverage and screening results explain whether comparisons are supported by the records.
