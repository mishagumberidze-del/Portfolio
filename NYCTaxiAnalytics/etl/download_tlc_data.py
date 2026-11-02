"""Download unmodified TLC sources, validate them, and inventory Parquet schemas.

Python 3.10+; install etl/requirements.txt. No trip rows are exported to CSV.
Run with --preflight-only to check every URL and disk capacity without downloading.
"""
from __future__ import annotations

import argparse
import concurrent.futures as cf
import csv
import hashlib
from http.client import HTTPException
import json
import logging
import os
from pathlib import Path
import random
import re
import shutil
import sys
import time
from datetime import datetime, timezone
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen
import zipfile

import pyarrow.parquet as pq

PAGE = "https://www.nyc.gov/site/tlc/about/tlc-trip-record-data.page"
HOST = "https://d37ci6vzurychx.cloudfront.net"
PREFIXES = {"yellow": "yellow", "green": "green", "fhv": "fhv", "hvfhv": "fhvhv"}
REFERENCES = {
    "taxi_zone_lookup.csv": HOST + "/misc/taxi_zone_lookup.csv",
    "taxi_zones.zip": HOST + "/misc/taxi_zones.zip",
    **{f"data_dictionary_trip_records_{x}.pdf":
       f"https://www.nyc.gov/assets/tlc/downloads/pdf/data_dictionary_trip_records_{x}.pdf"
       for x in ("yellow", "green", "fhv", "hvfhs")},
    "trip_record_user_guide.pdf": "https://www.nyc.gov/assets/tlc/downloads/pdf/trip_record_user_guide.pdf",
    "working_parquet_format.pdf": "https://www.nyc.gov/assets/tlc/downloads/pdf/working_parquet_format.pdf",
}
LOG = logging.getLogger("tlc")


def now():
    return datetime.now(timezone.utc).isoformat()


def save_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_suffix(path.suffix + ".tmp")
    tmp.write_text(json.dumps(value, indent=2) + "\n", encoding="utf-8")
    tmp.replace(path)


def items(year):
    result = []
    for kind, prefix in PREFIXES.items():
        for month in range(1, 13):
            name = f"{prefix}_tripdata_{year}-{month:02d}.parquet"
            result.append(dict(kind=kind, month=f"{year}-{month:02d}",
                               filename=name, relative_path=f"{kind}/{name}",
                               url=f"{HOST}/trip-data/{name}"))
    result += [dict(kind="reference", filename=name, relative_path=f"reference/{name}", url=url)
               for name, url in REFERENCES.items()]
    return result


def request(url, method="GET", headers=None):
    return urlopen(Request(url, method=method, headers={
        "User-Agent": "NYCTaxiAnalytics/1.0 (official-source portfolio ingestion)",
        "Accept-Encoding": "identity", **(headers or {})}), timeout=90)


def retry(operation, attempts=5):
    for attempt in range(attempts):
        try:
            return operation()
        except HTTPError as exc:
            if exc.code not in (408, 429, 500, 502, 503, 504) or attempt == attempts - 1:
                raise
            delay = min(60, int(exc.headers.get("Retry-After", "0"))
                        if exc.headers.get("Retry-After", "").isdigit() else 2 ** attempt)
        except (URLError, TimeoutError, ConnectionError, OSError, HTTPException) as exc:
            if attempt == attempts - 1:
                raise
            delay = 2 ** attempt
        LOG.warning("Transient failure; retry %s/%s in %.1fs", attempt + 1, attempts, delay)
        time.sleep(delay + random.random())


def head(item):
    def attempt():
        with request(item["url"], "HEAD") as response:
            if response.status != 200:
                raise ValueError(f"Unexpected HTTP status {response.status}")
            size = int(response.headers.get("Content-Length", "0"))
            if size <= 0:
                raise ValueError("Server did not supply a positive Content-Length; refusing unknown disk requirement")
            return {**item, "remote_bytes": size, "etag": response.headers.get("ETag"),
                    "last_modified": response.headers.get("Last-Modified"), "http_status": response.status}
    try:
        return retry(attempt)
    except Exception as exc:
        return {**item, "error": str(exc)}


def hashes(path):
    sha, md5 = hashlib.sha256(), hashlib.md5()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(8 * 1024 * 1024), b""):
            sha.update(chunk)
            md5.update(chunk)
    return sha.hexdigest(), md5.hexdigest()


def inspect(path, full=False):
    if path.suffix == ".parquet":
        with path.open("rb") as stream:
            if stream.read(4) != b"PAR1":
                raise ValueError("Missing Parquet header")
            stream.seek(-4, 2)
            if stream.read(4) != b"PAR1":
                raise ValueError("Missing Parquet footer (truncated file)")
        parquet = pq.ParquetFile(path, page_checksum_verification=True)
        schema = [{"name": field.name, "type": str(field.type), "nullable": field.nullable}
                  for field in parquet.schema_arrow]
        result = {"rows": parquet.metadata.num_rows, "row_groups": parquet.metadata.num_row_groups,
                  "schema": schema, "parquet_schema": str(parquet.schema),
                  "validation": "footer_and_schema"}
        if full:
            decoded = 0
            for batch in parquet.iter_batches(batch_size=65536, use_threads=False):
                batch.validate(full=True)
                decoded += batch.num_rows
            if decoded != result["rows"]:
                raise ValueError("Decoded row count differs from Parquet metadata")
            result.update(decoded_rows=decoded, validation="all_rows_all_columns_decoded")
        return result
    if path.suffix == ".zip":
        with zipfile.ZipFile(path) as archive:
            bad = archive.testzip()
            if bad:
                raise ValueError(f"ZIP CRC failed: {bad}")
            members = archive.namelist()
            for suffix in (".shp", ".shx", ".dbf", ".prj"):
                if not any(name.endswith(suffix) for name in members):
                    raise ValueError(f"Missing shapefile component {suffix}")
        return {"members": members, "validation": "zip_crc_all_members"}
    if path.suffix == ".csv":
        with path.open(encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream)
            rows = list(reader)
            if not {"LocationID", "Borough", "Zone", "service_zone"}.issubset(reader.fieldnames or []):
                raise ValueError("Unexpected zone lookup schema")
            if not rows or len({r['LocationID'] for r in rows}) != len(rows):
                raise ValueError("Empty zone lookup or duplicate LocationID")
            return {"rows": len(rows), "columns": reader.fieldnames, "validation": "csv_parsed_unique_zone_ids"}
    if path.suffix == ".pdf":
        with path.open("rb") as stream:
            if stream.read(5) != b"%PDF-":
                raise ValueError("Invalid PDF header")
            stream.seek(max(0, path.stat().st_size - 2048))
            if b"%%EOF" not in stream.read():
                raise ValueError("Missing PDF EOF")
        return {"validation": "pdf_header_and_eof"}
    raise ValueError(f"Unsupported source format: {path}")


def valid_existing(path, item, previous):
    if not path.exists() or path.stat().st_size != item["remote_bytes"]:
        return False
    try:
        inspect(path)
        sha, md5 = hashes(path)
        etag = (item.get("etag") or "").strip('"')
        if previous and previous.get("sha256") == sha and previous.get("etag") == item.get("etag"):
            return True
        if re.fullmatch(r"[0-9a-fA-F]{32}", etag):
            return md5.lower() == etag.lower()
        if previous:
            return False
        # An existing source without a manifest can be adopted after size and full
        # decoding validation; pin its SHA-256 and current ETag in the new manifest.
        inspect(path, full=True)
        return True
    except Exception:
        return False


def download(item, raw, previous, full):
    path = raw / item["relative_path"]
    path.parent.mkdir(parents=True, exist_ok=True)
    skipped = valid_existing(path, item, previous)
    if skipped:
        LOG.info("SKIP complete %s (%s bytes)", item['filename'], item['remote_bytes'])
    else:
        # Never publish a partial file; never replace the original until validation succeeds.
        partial = path.with_name(path.name + ".part")
        def attempt():
            received = 0
            sha, md5 = hashlib.sha256(), hashlib.md5()
            last_progress = time.monotonic()
            with request(item['url'], headers={"If-Match": item['etag']} if item.get('etag') else {}) as response:
                if response.status != 200:
                    raise ValueError(f"Unexpected GET HTTP status {response.status}")
                if response.headers.get("ETag") != item.get("etag"):
                    raise ValueError("Source ETag changed since preflight; rerun preflight")
                with partial.open("wb") as target:
                    while chunk := response.read(4 * 1024 * 1024):
                        target.write(chunk)
                        sha.update(chunk)
                        md5.update(chunk)
                        received += len(chunk)
                        if time.monotonic() - last_progress >= 10:
                            LOG.info("PROGRESS %s %.1f%% (%s/%s bytes)", item['filename'],
                                     100 * received / item['remote_bytes'], received, item['remote_bytes'])
                            last_progress = time.monotonic()
                    target.flush()
                    os.fsync(target.fileno())
            if received != item['remote_bytes']:
                raise OSError(f"Incomplete download: {received}/{item['remote_bytes']} bytes")
            etag = (item.get('etag') or '').strip('"')
            if re.fullmatch(r"[0-9a-fA-F]{32}", etag) and md5.hexdigest().lower() != etag.lower():
                raise OSError("ETag/MD5 integrity mismatch")
            # Validate under the real extension, while keeping the original file intact.
            return sha.hexdigest()
        sha = retry(attempt)
        # PyArrow detects format by content; references need their original extension.
        candidate = path.with_name(path.stem + ".candidate" + path.suffix)
        partial.replace(candidate)
        try:
            details = inspect(candidate, full=full)
            candidate.replace(path)
        except Exception:
            candidate.replace(partial)
            raise
        LOG.info("DOWNLOADED %s (%s bytes)", item['filename'], item['remote_bytes'])
    if skipped:
        sha, _ = hashes(path)
        details = inspect(path, full=full)
    LOG.info("VALIDATED %s rows=%s mode=%s", item['filename'], details.get('rows', '-'), details['validation'])
    return {**item, **details, "sha256": sha, "local_bytes": path.stat().st_size,
            "action": "skipped" if skipped else "downloaded", "verified_utc": now()}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--year', type=int, default=2024)
    parser.add_argument('--raw-dir', type=Path, default=Path(__file__).resolve().parents[1] / 'data' / 'raw')
    parser.add_argument('--workers', type=int, default=3)
    parser.add_argument('--reserve-gib', type=float, default=5)
    parser.add_argument('--preflight-only', action='store_true')
    parser.add_argument('--footer-only', action='store_true', help='Skip full row decoding (reported explicitly)')
    args = parser.parse_args()
    if args.workers < 1 or args.reserve_gib < 0:
        parser.error('workers must be positive and reserve-gib nonnegative')
    root = Path(__file__).resolve().parents[1]
    raw = args.raw_dir.resolve()
    raw.mkdir(parents=True, exist_ok=True)
    reports = root / 'reports' / f'tlc_{args.year}'
    reports.mkdir(parents=True, exist_ok=True)
    logs = root / 'logs'
    logs.mkdir(exist_ok=True)
    logging.basicConfig(level=logging.INFO, format='%(asctime)s %(levelname)s %(message)s',
                        handlers=[logging.StreamHandler(sys.stdout), logging.FileHandler(logs / 'download_tlc_data.log', encoding='utf-8')])
    lock = raw / '.download.lock'
    try:
        handle = lock.open('x')
    except FileExistsError:
        raise SystemExit(f'Downloader lock exists: {lock}. Check for a running process before removing a stale lock.')
    handle.write(str(os.getpid()))
    handle.close()
    try:
        run(args, raw, reports)
    finally:
        lock.unlink(missing_ok=True)


def run(args, raw, reports):
    LOG.info('Checking all 48 trip URLs and %s references before download', len(REFERENCES))
    with cf.ThreadPoolExecutor(max_workers=8) as pool:
        remote = list(pool.map(head, items(args.year)))
    failures = [x for x in remote if 'error' in x]
    total = sum(x.get('remote_bytes', 0) for x in remote)
    manifest_path = reports / 'manifest.json'
    previous = {x['relative_path']: x for x in json.loads(manifest_path.read_text(encoding='utf-8')).get('files', [])} if manifest_path.exists() else {}
    # Size-matching existing files may still need replacement; reserve the full size unless verified.
    required = sum(x['remote_bytes'] for x in remote if 'error' not in x and
                   not valid_existing(raw / x['relative_path'], x, previous.get(x['relative_path'])))
    disk = shutil.disk_usage(raw)
    preflight = dict(checked_utc=now(), source_page=PAGE, year=args.year, raw_dir=str(raw),
                     expected_trip_files=48, reachable_trip_files=sum(x['kind'] != 'reference' and 'error' not in x for x in remote),
                     total_source_bytes=total, required_download_bytes=required,
                     disk_free_bytes=disk.free, reserve_bytes=int(args.reserve_gib * 1024 ** 3), files=remote)
    save_json(reports / 'preflight.json', preflight)
    for kind in [*PREFIXES, 'reference']:
        LOG.info('PREFLIGHT %s: %s files, %s bytes', kind,
                 sum(x['kind'] == kind and 'error' not in x for x in remote),
                 sum(x.get('remote_bytes', 0) for x in remote if x['kind'] == kind))
    LOG.info('Disk free %.2f GiB; download needed %.2f GiB; reserve %.2f GiB', disk.free / 1024**3, required / 1024**3, args.reserve_gib)
    if failures:
        raise SystemExit(f'Preflight failed for {len(failures)} URLs; see {reports / "preflight.json"}. No downloads started.')
    if disk.free < required + preflight['reserve_bytes']:
        raise SystemExit('INSUFFICIENT DISK SPACE: downloads stopped before starting.')
    if args.preflight_only:
        return
    if not (reports / 'acquisition_preflight.json').exists():
        save_json(reports / 'acquisition_preflight.json', preflight)
    completed, errors = [], []
    def persist():
        save_json(manifest_path, dict(source_page=PAGE, year=args.year, raw_dir=str(raw), updated_utc=now(),
                                     files=sorted(completed, key=lambda x: x['relative_path']), failures=errors))
    with cf.ThreadPoolExecutor(max_workers=args.workers) as pool:
        futures = {pool.submit(download, item, raw, previous.get(item['relative_path']), not args.footer_only): item for item in remote}
        for future in cf.as_completed(futures):
            item = futures[future]
            try:
                completed.append(future.result())
            except Exception as exc:
                errors.append(dict(filename=item['filename'], error=str(exc)))
                LOG.exception('FAILED %s', item['filename'])
            persist()
            LOG.info('FILES %s/%s complete; failures=%s', len(completed), len(remote), len(errors))
    counts = {kind: len(list((raw / kind).glob('*.parquet'))) for kind in PREFIXES}
    unexpected = sorted(str(p.relative_to(raw)) for p in raw.rglob('*.parquet')
                        if p.relative_to(raw).as_posix() not in {x['relative_path'] for x in remote})
    partials = sorted(str(p.relative_to(raw)) for p in raw.rglob('*') if p.is_file() and (p.name.endswith('.part') or '.candidate.' in p.name))
    summary = dict(completed_utc=now(), file_counts=counts, verified_files=len(completed),
                   total_source_bytes=sum(x['local_bytes'] for x in completed),
                   disk_free_bytes_remaining=shutil.disk_usage(raw).free, unexpected_parquet=unexpected,
                   partial_files=partials, failures=errors,
                   row_totals={kind: sum(x.get('rows', 0) for x in completed if x['kind'] == kind) for kind in PREFIXES},
                   full_decode=not args.footer_only)
    save_json(reports / 'summary.json', summary)
    # Inventory only: no source trip rows or converted source data.
    trip_files = sorted((x for x in completed if x['kind'] in PREFIXES), key=lambda x: (x['month'], x['kind']))
    with (reports / 'monthly_inventory.csv').open('w', newline='', encoding='utf-8') as stream:
        writer = csv.DictWriter(stream, fieldnames=['kind', 'month', 'filename', 'rows', 'local_bytes', 'sha256', 'validation'])
        writer.writeheader()
        writer.writerows({key: x[key] for key in writer.fieldnames} for x in trip_files)
    schemas = {}
    for kind in PREFIXES:
        variants = {}
        for item in trip_files:
            if item['kind'] == kind:
                key = json.dumps(item['schema'], sort_keys=True)
                variants.setdefault(key, dict(schema=item['schema'], months=[]))['months'].append(item['month'])
        schemas[kind] = list(variants.values())
    save_json(reports / 'schemas.json', schemas)
    LOG.info('SUMMARY %s', json.dumps(summary))
    if errors or unexpected or partials or counts != dict.fromkeys(PREFIXES, 12) or len(completed) != 48 + len(REFERENCES):
        raise SystemExit('Inventory validation failed; see summary.json')


if __name__ == '__main__':
    main()
