"""Render a Markdown inspection report from the downloader's verified inventory."""
import csv
import json
from pathlib import Path
import shutil
import struct
import zipfile

ROOT = Path(__file__).resolve().parents[1]
REPORTS = ROOT / 'reports' / 'tlc_2024'


def main():
    manifest = json.loads((REPORTS / 'manifest.json').read_text(encoding='utf-8'))
    summary = json.loads((REPORTS / 'summary.json').read_text(encoding='utf-8'))
    preflight_path = REPORTS / 'acquisition_preflight.json'
    preflight = json.loads((preflight_path if preflight_path.exists() else REPORTS / 'preflight.json').read_text(encoding='utf-8'))
    if not summary['full_decode']:
        raise SystemExit('Run the downloader with full decoding before building the complete inspection report.')
    schemas = json.loads((REPORTS / 'schemas.json').read_text(encoding='utf-8'))
    raw = Path(manifest['raw_dir'])
    files = manifest['files']
    kinds = ['yellow', 'green', 'fhv', 'hvfhv']
    lines = ['# NYC TLC 2024 source acquisition and inspection', '',
             f"Verified: {summary['completed_utc']}", '',
             '[Official TLC source page](https://www.nyc.gov/site/tlc/about/tlc-trip-record-data.page)', '',
             f'Raw directory: `{raw}`', '',
             '## Acquisition and completeness', '',
             f"- Expected/reachable trip URLs in preflight: 48/{preflight['reachable_trip_files']}.",
             f"- Preflight source size: {preflight['total_source_bytes']:,} bytes ({preflight['total_source_bytes']/1024**3:.4f} GiB).",
             f"- Free disk at preflight: {preflight['disk_free_bytes']:,} bytes ({preflight['disk_free_bytes']/1024**3:.2f} GiB); 5 GiB reserve enforced.",
             f"- Verified files: {len(files)} ({sum(x['kind'] in kinds for x in files)} Parquet + {sum(x['kind'] == 'reference' for x in files)} references).",
             f"- Source files occupy {summary['total_source_bytes']:,} bytes ({summary['total_source_bytes']/1e9:.6f} GB; {summary['total_source_bytes']/1024**3:.6f} GiB), logical file lengths.",
             f"- Free disk when this report was generated: {shutil.disk_usage(raw).free:,} bytes ({shutil.disk_usage(raw).free/1024**3:.2f} GiB).",
             f"- Failures: {len(summary['failures'])}; unexpected Parquet: {len(summary['unexpected_parquet'])}; incomplete files: {len(summary['partial_files'])}.", '',
             '| Type | Files | Rows | Bytes | GiB |', '|---|---:|---:|---:|---:|']
    for kind in kinds:
        subset = [x for x in files if x['kind'] == kind]
        size = sum(x['local_bytes'] for x in subset)
        lines.append(f"| {kind.upper()} | {len(subset)} | {sum(x['rows'] for x in subset):,} | {size:,} | {size/1024**3:.6f} |")
    lines += ['', 'All trip files were checked against the server Content-Length, pinned to the preflight ETag through a conditional GET, hashed with SHA-256, parsed for footer/schema metadata, and fully decoded in bounded batches across every row and column. Decoded and metadata row counts match. Page checksums were checked where present. Single-part ETags were compared to MD5; multipart ETags are source identifiers, not whole-file checksums. TLC does not provide independent SHA-256 checksums, so the manifest records the acquired local hashes for future integrity checks.', '',
              'These checks establish transport completeness and Parquet readability. They do not establish that the provider submitted every real-world trip or that every reported value is correct. No trip data has been rewritten, filtered, converted, or loaded into SQL Server. Raw files and logs are excluded from Git by the project .gitignore.', '',
              '## Row counts by source-file month', '',
              'Months below refer to official file partitions, not a regrouping of actual pickup timestamps. Source files may contain out-of-period timestamps; temporal quality profiling is a later step.', '',
              '| Month | Yellow | Green | FHV | HVFHV |', '|---|---:|---:|---:|---:|']
    for month in range(1, 13):
        key = f'2024-{month:02d}'
        lines.append('| ' + key + ' | ' + ' | '.join(f"{next(x['rows'] for x in files if x['kind'] == kind and x.get('month') == key):,}" for kind in kinds) + ' |')
    lines.append('| **Total** | ' + ' | '.join(f"**{summary['row_totals'][kind]:,}**" for kind in kinds) + ' |')
    lines += ['', '## File sizes by source-file month (bytes)', '', '| Month | Yellow | Green | FHV | HVFHV |', '|---|---:|---:|---:|---:|']
    for month in range(1, 13):
        key = f'2024-{month:02d}'
        lines.append('| ' + key + ' | ' + ' | '.join(f"{next(x['local_bytes'] for x in files if x['kind'] == kind and x.get('month') == key):,}" for kind in kinds) + ' |')
    lines += ['', 'Exact file-level URLs, ETags, Last-Modified values, byte lengths, SHA-256 hashes, row groups, and physical Parquet schemas are retained in `manifest.json`; `monthly_inventory.csv` is a small metadata inventory, not converted trip records.', '',
              '## Actual Parquet schemas', '',
              'Arrow logical types below come from the actual local Parquet files. All schema variants across all twelve months are listed. Nullability is the schema declaration, not a measurement of observed null frequency. `timestamp[us]` has no embedded timezone. Source spelling and capitalization are preserved.', '']
    for kind in kinds:
        lines += [f'### {kind.upper()}', '', f'{len(schemas[kind])} distinct schema variant(s).', '']
        for index, variant in enumerate(schemas[kind], 1):
            lines += [f"Variant {index}; months: {', '.join(variant['months'])}; columns: {len(variant['schema'])}.", '',
                      '| Column | Arrow type | Nullable |', '|---|---|---|']
            lines += [f"| `{field['name']}` | `{field['type']}` | {field['nullable']} |" for field in variant['schema']]
            lines.append('')
    lines += ['## Important schema and dictionary differences', '',
              '- Yellow and Green are metered-trip datasets with passenger counts, payment/rate codes, trip distance, and itemized taxi fares. Yellow uses `tpep_*` timestamps and `Airport_fee`; Green uses `lpep_*` timestamps plus `ehail_fee` and `trip_type`. Do not lose these distinctions in ingestion.',
              '- FHV is a minimal dispatch record: dispatching/affiliated base, pickup/dropoff times, pickup/dropoff zone IDs, and `SR_Flag`. It has no passenger count, distance, fare, payment type, tips, or driver pay. Missing metrics are unavailable, not zero.',
              '- HVFHV is a richer high-volume service record with business/base identifiers, request/on-scene/pickup/dropoff timestamps, trip miles/seconds, fare components, driver pay, and separate sharing/accessibility flags. Its fare concepts are not identical to taxi meter fares.',
              '- Source casing differs: FHV has `dropOff_datetime`, `PUlocationID`, `DOlocationID`, and `Affiliated_base_number`; HVFHV has `dropoff_datetime`, `PULocationID`, and `DOLocationID`. Use explicit per-source mappings.',
              '- Source type widths must come from the tables above: FHV zone IDs and taxi categorical fields do not necessarily use the same integer width. Preserve the source type in staging and validate before narrowing.',
              '- The four dictionaries linked by TLC are dated March 18, 2025, rather than being frozen 2024 specifications. They describe `cbd_congestion_fee` for 2025 onward. Its presence in a dictionary must not create a synthetic source field in 2024.',
              '- The Yellow dictionary spells `airport_fee` in lowercase; the inspected 2024 source spells `Airport_fee`. The Green sources include `ehail_fee`, which is absent from the current Green dictionary. Actual file schemas govern ingestion.',
              '- The FHV dictionary says high-volume records have been split into a separate dataset since 2019. Treat these as distinct published 2024 populations; do not assume the FHV file is an inclusive parent containing the HVFHV file.',
              '- Taxi `tip_amount` omits cash tips according to the dictionaries. FHV `SR_Flag` has documented null/shared-ride semantics and historical caveats; it is not automatically equivalent to the separate HVFHV requested/matched flags.',
              '- The 2019 user guide is historical documentation. Its old CSV/download schedule and historical company/base roster do not override the current Parquet source page or establish 2024 company activity.', '',
              '## Reference files', '', '| File | Bytes | Validation |', '|---|---:|---|']
    for item in files:
        if item['kind'] == 'reference':
            lines.append(f"| [{item['filename']}]({item['url']}) | {item['local_bytes']:,} | {item['validation']} |")
    with (raw / 'reference' / 'taxi_zone_lookup.csv').open(encoding='utf-8-sig', newline='') as stream:
        zones = list(csv.DictReader(stream))
    with zipfile.ZipFile(raw / 'reference' / 'taxi_zones.zip') as archive:
        members = archive.namelist()
        dbf = archive.read(next(x for x in members if x.endswith('.dbf')))
        shape = archive.read(next(x for x in members if x.endswith('.shp')))
        crs = archive.read(next(x for x in members if x.endswith('.prj'))).decode()
    ref_details = dict(zone_lookup_rows=len(zones), lookup_tail=zones[-2:],
                       shapefile_features=struct.unpack_from('<I', dbf, 4)[0],
                       shapefile_bounds=struct.unpack_from('<4d', shape, 36),
                       shapefile_crs_wkt=crs, shapefile_members=members)
    (REPORTS / 'reference_details.json').write_text(json.dumps(ref_details, indent=2) + '\n', encoding='utf-8')
    lines += ['', f"The lookup has {len(zones)} unique LocationIDs; the geographic DBF has {ref_details['shapefile_features']} features. Lookup tail: " + '; '.join(f"{x['LocationID']} = {x['Borough']} / {x['Zone']}" for x in zones[-2:]) + '.', '',
              'The original `taxi_zones.zip` contains the .shp/.shx/.dbf/.prj components and additional metadata. It is an actual shapefile ZIP even though the source page labels the link PARQUET. All ZIP member CRCs passed. The original ZIP remains unchanged. Full member list, bounds, and CRS WKT are recorded in `reference_details.json`.', '',
              '## Recommended SQL Server model (design recommendation only)', '',
              'Use `Portfolio_NYCTaxi` with four source-specific staging tables: `stg.YellowTrip`, `stg.GreenTrip`, `stg.FHVTrip`, and `stg.HVFHVTrip`. Retain original field meanings, data types, nulls, and explicit mappings. Preserve immutable files as the replay source.', '',
              'Track each source file in an ingestion ledger with type, source month, URL, ETag, byte length, SHA-256, load batch, timestamps, and verified/loaded row counts. Add source-file and stable file-row lineage during loading, and enforce file/batch idempotency. No global natural trip ID is supplied; do not remove legitimate rows by deduplicating on pickup time, zones, or fares.', '',
              'Build separate core fact tables for the four trip types at their documented row grain. Share conformed date/time and zone dimensions, and use provider/business/base dimensions only where appropriate. Use the zone dimension in pickup and dropoff roles. Preserve special lookup IDs and provide an explicit unknown member for null/unmatched IDs; avoid dropping trips through inner joins to geographic polygons.', '',
              'Use `datetime2(6)` for microsecond timestamps; preserve timezone-naive source values and define NYC local-time/DST rules explicitly before deriving UTC. SQL Server supports configurable fractional-second precision ([Microsoft datetime2 documentation](https://learn.microsoft.com/en-us/sql/t-sql/data-types/datetime2-transact-sql)). Keep `float(53)` for source doubles in staging; select decimal precision/scale for core monetary amounts only after range/precision profiling. Preserve negatives and nullable/categorical flags for validation rather than imposing assumptions.', '',
              'The shapefile CRS is a projected NAD83 New York Long Island coordinate system in US survey feet (see the exact WKT). Preserve it in SQL Server `geometry` with a verified SRID. For `geography`/longitude-latitude use, reproject coordinates to WGS84 first; changing an SRID label does not transform coordinates.', '',
              'Expose harmonized reporting views for comparable trip counts, timestamps, zones, and carefully aligned distance measures; retain source type and use NULL for unavailable measures. Keep source-specific fare/payment/driver-pay reporting. Define comparable revenue measures before aggregating across services.', '',
              'For the large fact tables, evaluate clustered columnstore and monthly partitioning against the SQL Server edition and expected queries. Columnstore is intended for analytics/fact-table scans ([Microsoft columnstore overview](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/columnstore-indexes-overview)). Size the database, indexes, transaction log, backups, and ETL workspace separately; compressed source size is not a SQL Server storage estimate. Current disk sufficiency applies to raw acquisition only.', '',
              'Before creating the final model, profile nulls, observed flag domains, zone referential coverage, date ranges/out-of-period rows, negative/zero distance and fare values, duplicate candidates, and financial ranges. Full Parquet decoding is complete; these semantic quality checks and database/dashboard work are outside this acquisition step.', '',
              '## Problems and rerun behavior', '']
    if summary['failures'] or summary['unexpected_parquet'] or summary['partial_files']:
        lines += ['```json', json.dumps({k: summary[k] for k in ['failures', 'unexpected_parquet', 'partial_files']}, indent=2), '```']
    else:
        lines += ['No failed, incomplete, unexpected, or unreadable source files remain. Any transient preflight HTTP failures were retried successfully; see the append-only log for acquisition history.']
    rerun_path = REPORTS / 'rerun_verification.json'
    if rerun_path.exists():
        rerun = json.loads(rerun_path.read_text(encoding='utf-8-sig'))
        lines += ['', f"An actual downloader rerun skipped all {rerun['skipped_on_rerun']} source files and fully decoded all {rerun['fully_decoded_parquet_on_rerun']} Parquet files again. All source file sizes and modification times remained unchanged: {rerun['all_source_sizes_and_mtimes_unchanged']}. Details and the before-rerun snapshot are in `rerun_verification.json`. The original acquisition manifest is preserved in `acquisition_manifest.json`."]
    lines += ['', 'The downloader uses a lock and atomically publishes validated files. Reruns verify existing SHA-256/source identifiers and skip complete sources. Six offline safety tests check URL coverage, truncation detection, local corruption detection, preservation of an existing file on a short transfer, skipping a complete file without changing its modification time or sending a GET, and stopping before download when disk space is insufficient.', '']
    (REPORTS / 'inspection_report.md').write_text('\n'.join(lines), encoding='utf-8')
    print(REPORTS / 'inspection_report.md')


if __name__ == '__main__':
    main()
