# NYC TLC 2024 source acquisition and inspection

Verified: 2026-11-02T08:57:40.561784+00:00

[Official TLC source page](https://www.nyc.gov/site/tlc/about/tlc-trip-record-data.page)

Raw directory: `C:\Data\NYCTaxiAnalytics\data\raw`

## Acquisition and completeness

- Expected/reachable trip URLs in preflight: 48/48.
- Preflight source size: 6,688,082,712 bytes (6.2288 GiB).
- Free disk at preflight: 56,036,470,784 bytes (52.19 GiB); 5 GiB reserve enforced.
- Verified files: 56 (48 Parquet + 8 references).
- Source files occupy 6,688,082,712 bytes (6.688083 GB; 6.228762 GiB), logical file lengths.
- Free disk when this report was generated: 48,620,367,872 bytes (45.28 GiB).
- Failures: 0; unexpected Parquet: 0; incomplete files: 0.

| Type | Files | Rows | Bytes | GiB |
|---|---:|---:|---:|---:|
| YELLOW | 12 | 41,169,720 | 693,001,713 | 0.645408 |
| GREEN | 12 | 660,218 | 15,920,032 | 0.014827 |
| FHV | 12 | 17,630,326 | 201,238,012 | 0.187418 |
| HVFHV | 12 | 239,470,448 | 5,776,046,623 | 5.379363 |

All trip files were checked against the server Content-Length, pinned to the preflight ETag through a conditional GET, hashed with SHA-256, parsed for footer/schema metadata, and fully decoded in bounded batches across every row and column. Decoded and metadata row counts match. Page checksums were checked where present. Single-part ETags were compared to MD5; multipart ETags are source identifiers, not whole-file checksums. TLC does not provide independent SHA-256 checksums, so the manifest records the acquired local hashes for future integrity checks.

These checks establish transport completeness and Parquet readability. They do not establish that the provider submitted every real-world trip or that every reported value is correct. No trip data has been rewritten, filtered, converted, or loaded into SQL Server. Raw files and logs are excluded from Git by the project .gitignore.

## Row counts by source-file month

Months below refer to official file partitions, not a regrouping of actual pickup timestamps. Source files may contain out-of-period timestamps; temporal quality profiling is a later step.

| Month | Yellow | Green | FHV | HVFHV |
|---|---:|---:|---:|---:|
| 2024-01 | 2,964,624 | 56,551 | 1,290,116 | 19,663,930 |
| 2024-02 | 3,007,526 | 53,577 | 1,176,093 | 19,359,148 |
| 2024-03 | 3,582,628 | 57,457 | 1,469,352 | 21,280,788 |
| 2024-04 | 3,514,289 | 56,471 | 1,444,626 | 19,733,038 |
| 2024-05 | 3,723,833 | 61,003 | 1,352,502 | 20,704,538 |
| 2024-06 | 3,539,193 | 54,748 | 1,386,539 | 20,123,226 |
| 2024-07 | 3,076,903 | 51,837 | 1,382,739 | 19,182,934 |
| 2024-08 | 2,979,183 | 51,771 | 1,484,471 | 19,128,392 |
| 2024-09 | 3,633,030 | 54,440 | 1,718,375 | 19,209,788 |
| 2024-10 | 3,833,771 | 56,147 | 1,421,231 | 20,028,282 |
| 2024-11 | 3,646,369 | 52,222 | 1,591,082 | 19,987,533 |
| 2024-12 | 3,668,371 | 53,994 | 1,913,200 | 21,068,851 |
| **Total** | **41,169,720** | **660,218** | **17,630,326** | **239,470,448** |

## File sizes by source-file month (bytes)

| Month | Yellow | Green | FHV | HVFHV |
|---|---:|---:|---:|---:|
| 2024-01 | 49,961,641 | 1,362,284 | 15,023,585 | 472,757,547 |
| 2024-02 | 50,349,284 | 1,283,805 | 13,608,941 | 462,475,428 |
| 2024-03 | 60,078,280 | 1,372,372 | 16,743,352 | 507,940,560 |
| 2024-04 | 59,133,625 | 1,346,502 | 16,515,459 | 476,115,643 |
| 2024-05 | 62,553,128 | 1,453,912 | 15,958,984 | 498,575,918 |
| 2024-06 | 59,859,922 | 1,326,194 | 16,029,924 | 484,417,715 |
| 2024-07 | 52,299,432 | 1,250,973 | 15,957,854 | 468,437,522 |
| 2024-08 | 51,067,350 | 1,267,079 | 16,895,389 | 465,991,646 |
| 2024-09 | 61,170,186 | 1,326,186 | 19,146,083 | 466,470,118 |
| 2024-10 | 64,346,071 | 1,353,731 | 16,560,601 | 485,102,359 |
| 2024-11 | 60,658,709 | 1,264,688 | 18,117,293 | 480,228,052 |
| 2024-12 | 61,524,085 | 1,312,306 | 20,680,547 | 507,534,115 |

Exact file-level URLs, ETags, Last-Modified values, byte lengths, SHA-256 hashes, row groups, and physical Parquet schemas are retained in `manifest.json`; `monthly_inventory.csv` is a small metadata inventory, not converted trip records.

## Actual Parquet schemas

Arrow logical types below come from the actual local Parquet files. All schema variants across all twelve months are listed. Nullability is the schema declaration, not a measurement of observed null frequency. `timestamp[us]` has no embedded timezone. Source spelling and capitalization are preserved.

### YELLOW

1 distinct schema variant(s).

Variant 1; months: 2024-01, 2024-02, 2024-03, 2024-04, 2024-05, 2024-06, 2024-07, 2024-08, 2024-09, 2024-10, 2024-11, 2024-12; columns: 19.

| Column | Arrow type | Nullable |
|---|---|---|
| `VendorID` | `int32` | True |
| `tpep_pickup_datetime` | `timestamp[us]` | True |
| `tpep_dropoff_datetime` | `timestamp[us]` | True |
| `passenger_count` | `int64` | True |
| `trip_distance` | `double` | True |
| `RatecodeID` | `int64` | True |
| `store_and_fwd_flag` | `large_string` | True |
| `PULocationID` | `int32` | True |
| `DOLocationID` | `int32` | True |
| `payment_type` | `int64` | True |
| `fare_amount` | `double` | True |
| `extra` | `double` | True |
| `mta_tax` | `double` | True |
| `tip_amount` | `double` | True |
| `tolls_amount` | `double` | True |
| `improvement_surcharge` | `double` | True |
| `total_amount` | `double` | True |
| `congestion_surcharge` | `double` | True |
| `Airport_fee` | `double` | True |

### GREEN

1 distinct schema variant(s).

Variant 1; months: 2024-01, 2024-02, 2024-03, 2024-04, 2024-05, 2024-06, 2024-07, 2024-08, 2024-09, 2024-10, 2024-11, 2024-12; columns: 20.

| Column | Arrow type | Nullable |
|---|---|---|
| `VendorID` | `int32` | True |
| `lpep_pickup_datetime` | `timestamp[us]` | True |
| `lpep_dropoff_datetime` | `timestamp[us]` | True |
| `store_and_fwd_flag` | `large_string` | True |
| `RatecodeID` | `int64` | True |
| `PULocationID` | `int32` | True |
| `DOLocationID` | `int32` | True |
| `passenger_count` | `int64` | True |
| `trip_distance` | `double` | True |
| `fare_amount` | `double` | True |
| `extra` | `double` | True |
| `mta_tax` | `double` | True |
| `tip_amount` | `double` | True |
| `tolls_amount` | `double` | True |
| `ehail_fee` | `double` | True |
| `improvement_surcharge` | `double` | True |
| `total_amount` | `double` | True |
| `payment_type` | `int64` | True |
| `trip_type` | `int64` | True |
| `congestion_surcharge` | `double` | True |

### FHV

1 distinct schema variant(s).

Variant 1; months: 2024-01, 2024-02, 2024-03, 2024-04, 2024-05, 2024-06, 2024-07, 2024-08, 2024-09, 2024-10, 2024-11, 2024-12; columns: 7.

| Column | Arrow type | Nullable |
|---|---|---|
| `dispatching_base_num` | `large_string` | True |
| `pickup_datetime` | `timestamp[us]` | True |
| `dropOff_datetime` | `timestamp[us]` | True |
| `PUlocationID` | `int64` | True |
| `DOlocationID` | `int64` | True |
| `SR_Flag` | `int64` | True |
| `Affiliated_base_number` | `large_string` | True |

### HVFHV

1 distinct schema variant(s).

Variant 1; months: 2024-01, 2024-02, 2024-03, 2024-04, 2024-05, 2024-06, 2024-07, 2024-08, 2024-09, 2024-10, 2024-11, 2024-12; columns: 24.

| Column | Arrow type | Nullable |
|---|---|---|
| `hvfhs_license_num` | `large_string` | True |
| `dispatching_base_num` | `large_string` | True |
| `originating_base_num` | `large_string` | True |
| `request_datetime` | `timestamp[us]` | True |
| `on_scene_datetime` | `timestamp[us]` | True |
| `pickup_datetime` | `timestamp[us]` | True |
| `dropoff_datetime` | `timestamp[us]` | True |
| `PULocationID` | `int32` | True |
| `DOLocationID` | `int32` | True |
| `trip_miles` | `double` | True |
| `trip_time` | `int64` | True |
| `base_passenger_fare` | `double` | True |
| `tolls` | `double` | True |
| `bcf` | `double` | True |
| `sales_tax` | `double` | True |
| `congestion_surcharge` | `double` | True |
| `airport_fee` | `double` | True |
| `tips` | `double` | True |
| `driver_pay` | `double` | True |
| `shared_request_flag` | `large_string` | True |
| `shared_match_flag` | `large_string` | True |
| `access_a_ride_flag` | `large_string` | True |
| `wav_request_flag` | `large_string` | True |
| `wav_match_flag` | `large_string` | True |

## Important schema and dictionary differences

- Yellow and Green are metered-trip datasets with passenger counts, payment/rate codes, trip distance, and itemized taxi fares. Yellow uses `tpep_*` timestamps and `Airport_fee`; Green uses `lpep_*` timestamps plus `ehail_fee` and `trip_type`. Do not lose these distinctions in ingestion.
- FHV is a minimal dispatch record: dispatching/affiliated base, pickup/dropoff times, pickup/dropoff zone IDs, and `SR_Flag`. It has no passenger count, distance, fare, payment type, tips, or driver pay. Missing metrics are unavailable, not zero.
- HVFHV is a richer high-volume service record with business/base identifiers, request/on-scene/pickup/dropoff timestamps, trip miles/seconds, fare components, driver pay, and separate sharing/accessibility flags. Its fare concepts are not identical to taxi meter fares.
- Source casing differs: FHV has `dropOff_datetime`, `PUlocationID`, `DOlocationID`, and `Affiliated_base_number`; HVFHV has `dropoff_datetime`, `PULocationID`, and `DOLocationID`. Use explicit per-source mappings.
- Source type widths must come from the tables above: FHV zone IDs and taxi categorical fields do not necessarily use the same integer width. Preserve the source type in staging and validate before narrowing.
- The four dictionaries linked by TLC are dated March 18, 2025, rather than being frozen 2024 specifications. They describe `cbd_congestion_fee` for 2025 onward. Its presence in a dictionary must not create a synthetic source field in 2024.
- The Yellow dictionary spells `airport_fee` in lowercase; the inspected 2024 source spells `Airport_fee`. The Green sources include `ehail_fee`, which is absent from the current Green dictionary. Actual file schemas govern ingestion.
- The FHV dictionary says high-volume records have been split into a separate dataset since 2019. Treat these as distinct published 2024 populations; do not assume the FHV file is an inclusive parent containing the HVFHV file.
- Taxi `tip_amount` omits cash tips according to the dictionaries. FHV `SR_Flag` has documented null/shared-ride semantics and historical caveats; it is not automatically equivalent to the separate HVFHV requested/matched flags.
- The 2019 user guide is historical documentation. Its old CSV/download schedule and historical company/base roster do not override the current Parquet source page or establish 2024 company activity.

## Reference files

| File | Bytes | Validation |
|---|---:|---|
| [data_dictionary_trip_records_fhv.pdf](https://www.nyc.gov/assets/tlc/downloads/pdf/data_dictionary_trip_records_fhv.pdf) | 143,037 | pdf_header_and_eof |
| [data_dictionary_trip_records_green.pdf](https://www.nyc.gov/assets/tlc/downloads/pdf/data_dictionary_trip_records_green.pdf) | 148,750 | pdf_header_and_eof |
| [data_dictionary_trip_records_hvfhs.pdf](https://www.nyc.gov/assets/tlc/downloads/pdf/data_dictionary_trip_records_hvfhs.pdf) | 117,347 | pdf_header_and_eof |
| [data_dictionary_trip_records_yellow.pdf](https://www.nyc.gov/assets/tlc/downloads/pdf/data_dictionary_trip_records_yellow.pdf) | 148,556 | pdf_header_and_eof |
| [taxi_zone_lookup.csv](https://d37ci6vzurychx.cloudfront.net/misc/taxi_zone_lookup.csv) | 12,331 | csv_parsed_unique_zone_ids |
| [taxi_zones.zip](https://d37ci6vzurychx.cloudfront.net/misc/taxi_zones.zip) | 1,022,574 | zip_crc_all_members |
| [trip_record_user_guide.pdf](https://www.nyc.gov/assets/tlc/downloads/pdf/trip_record_user_guide.pdf) | 202,694 | pdf_header_and_eof |
| [working_parquet_format.pdf](https://www.nyc.gov/assets/tlc/downloads/pdf/working_parquet_format.pdf) | 81,043 | pdf_header_and_eof |

The lookup has 265 unique LocationIDs; the geographic DBF has 263 features. Lookup tail: 264 = Unknown / N/A; 265 = N/A / Outside of NYC.

The original `taxi_zones.zip` contains the .shp/.shx/.dbf/.prj components and additional metadata. It is an actual shapefile ZIP even though the source page labels the link PARQUET. All ZIP member CRCs passed. The original ZIP remains unchanged. Full member list, bounds, and CRS WKT are recorded in `reference_details.json`.

## Recommended SQL Server model (design recommendation only)

Use `Portfolio_NYCTaxi` with four source-specific staging tables: `stg.YellowTrip`, `stg.GreenTrip`, `stg.FHVTrip`, and `stg.HVFHVTrip`. Retain original field meanings, data types, nulls, and explicit mappings. Preserve immutable files as the replay source.

Track each source file in an ingestion ledger with type, source month, URL, ETag, byte length, SHA-256, load batch, timestamps, and verified/loaded row counts. Add source-file and stable file-row lineage during loading, and enforce file/batch idempotency. No global natural trip ID is supplied; do not remove legitimate rows by deduplicating on pickup time, zones, or fares.

Build separate core fact tables for the four trip types at their documented row grain. Share conformed date/time and zone dimensions, and use provider/business/base dimensions only where appropriate. Use the zone dimension in pickup and dropoff roles. Preserve special lookup IDs and provide an explicit unknown member for null/unmatched IDs; avoid dropping trips through inner joins to geographic polygons.

Use `datetime2(6)` for microsecond timestamps; preserve timezone-naive source values and define NYC local-time/DST rules explicitly before deriving UTC. SQL Server supports configurable fractional-second precision ([Microsoft datetime2 documentation](https://learn.microsoft.com/en-us/sql/t-sql/data-types/datetime2-transact-sql)). Keep `float(53)` for source doubles in staging; select decimal precision/scale for core monetary amounts only after range/precision profiling. Preserve negatives and nullable/categorical flags for validation rather than imposing assumptions.

The shapefile CRS is a projected NAD83 New York Long Island coordinate system in US survey feet (see the exact WKT). Preserve it in SQL Server `geometry` with a verified SRID. For `geography`/longitude-latitude use, reproject coordinates to WGS84 first; changing an SRID label does not transform coordinates.

Expose harmonized reporting views for comparable trip counts, timestamps, zones, and carefully aligned distance measures; retain source type and use NULL for unavailable measures. Keep source-specific fare/payment/driver-pay reporting. Define comparable revenue measures before aggregating across services.

For the large fact tables, evaluate clustered columnstore and monthly partitioning against the SQL Server edition and expected queries. Columnstore is intended for analytics/fact-table scans ([Microsoft columnstore overview](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/columnstore-indexes-overview)). Size the database, indexes, transaction log, backups, and ETL workspace separately; compressed source size is not a SQL Server storage estimate. Current disk sufficiency applies to raw acquisition only.

Before creating the final model, profile nulls, observed flag domains, zone referential coverage, date ranges/out-of-period rows, negative/zero distance and fare values, duplicate candidates, and financial ranges. Full Parquet decoding is complete; these semantic quality checks and database/dashboard work are outside this acquisition step.

## Problems and rerun behavior

No failed, incomplete, unexpected, or unreadable source files remain. Any transient preflight HTTP failures were retried successfully; see the append-only log for acquisition history.

An actual downloader rerun skipped all 56 source files and fully decoded all 48 Parquet files again. All source file sizes and modification times remained unchanged: True. Details and the before-rerun snapshot are in `rerun_verification.json`. The original acquisition manifest is preserved in `acquisition_manifest.json`.

The downloader uses a lock and atomically publishes validated files. Reruns verify existing SHA-256/source identifiers and skip complete sources. Six offline safety tests check URL coverage, truncation detection, local corruption detection, preservation of an existing file on a short transfer, skipping a complete file without changing its modification time or sending a GET, and stopping before download when disk space is insufficient.
