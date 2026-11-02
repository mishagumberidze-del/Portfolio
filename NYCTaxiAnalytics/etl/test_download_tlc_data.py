"""Offline checks of the downloader's file-safety and capacity safeguards."""
import argparse
import hashlib
import io
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import pyarrow as pa
import pyarrow.parquet as pq
import download_tlc_data as tlc


class DownloaderTests(unittest.TestCase):
    def test_expected_urls_and_hvfhv_prefix(self):
        sources = tlc.items(2024)
        trips = [x for x in sources if x['kind'] != 'reference']
        self.assertEqual(len(trips), 48)
        self.assertEqual(len({x['url'] for x in trips}), 48)
        self.assertEqual(sum(x['kind'] == 'hvfhv' for x in trips), 12)
        self.assertTrue(all('/fhvhv_tripdata_' in x['url'] for x in trips if x['kind'] == 'hvfhv'))

    def test_parquet_full_decode_and_truncation_detection(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'test.parquet'
            pq.write_table(pa.table({'id': [1, 2, None]}), path)
            self.assertEqual(tlc.inspect(path, full=True)['decoded_rows'], 3)
            path.write_bytes(path.read_bytes()[:-7])
            with self.assertRaises(ValueError):
                tlc.inspect(path, full=True)

    def test_hash_pinning_detects_local_change(self):
        with tempfile.TemporaryDirectory() as tmp:
            path = Path(tmp) / 'test.parquet'
            pq.write_table(pa.table({'id': [1, 2]}), path)
            sha, _ = tlc.hashes(path)
            item = dict(remote_bytes=path.stat().st_size, etag='"multipart-2"')
            self.assertTrue(tlc.valid_existing(path, item, dict(sha256=sha, etag=item['etag'])))
            self.assertFalse(tlc.valid_existing(path, item, dict(sha256='wrong', etag=item['etag'])))

    def test_short_transfer_preserves_existing_file(self):
        class Response(io.BytesIO):
            status = 200
            headers = {'ETag': '"abc-2"'}
        with tempfile.TemporaryDirectory() as tmp:
            raw = Path(tmp)
            existing = raw / 'test.parquet'
            existing.write_bytes(b'preserve original')
            item = dict(relative_path=existing.name, filename=existing.name, remote_bytes=100,
                        etag='"abc-2"', url='https://example.invalid/test')
            with patch.object(tlc, 'request', return_value=Response(b'short')), \
                 patch.object(tlc, 'retry', side_effect=lambda operation: operation()):
                with self.assertRaises(OSError):
                    tlc.download(item, raw, None, True)
            self.assertEqual(existing.read_bytes(), b'preserve original')
            self.assertTrue((raw / 'test.parquet.part').exists())

    def test_complete_file_skips_get_and_keeps_mtime(self):
        with tempfile.TemporaryDirectory() as tmp:
            raw = Path(tmp)
            path = raw / 'test.parquet'
            pq.write_table(pa.table({'id': [1, 2]}), path)
            sha, md5 = tlc.hashes(path)
            before = path.stat().st_mtime_ns
            item = dict(relative_path=path.name, filename=path.name, remote_bytes=path.stat().st_size,
                        etag='"' + md5 + '"', url='https://example.invalid/test')
            with patch.object(tlc, 'request') as request:
                result = tlc.download(item, raw, dict(sha256=sha, etag=item['etag']), True)
                request.assert_not_called()
            self.assertEqual(result['action'], 'skipped')
            self.assertEqual(result['decoded_rows'], 2)
            self.assertEqual(path.stat().st_mtime_ns, before)

    def test_insufficient_space_stops_before_download(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            args = argparse.Namespace(year=2024, reserve_gib=5, preflight_only=False)
            class Disk:
                free = 0
            def reachable(item):
                return {**item, 'remote_bytes': 100, 'etag': '"example-2"'}
            with patch.object(tlc, 'head', side_effect=reachable), \
                 patch.object(tlc.shutil, 'disk_usage', return_value=Disk()), \
                 patch.object(tlc, 'download') as download:
                with self.assertRaisesRegex(SystemExit, 'INSUFFICIENT DISK SPACE'):
                    tlc.run(args, root / 'raw', root / 'reports')
                download.assert_not_called()


if __name__ == '__main__':
    unittest.main()
