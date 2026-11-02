"""Validate untouched Olist CSVs and emit deterministic TSV files for MSSQL BCP.

Raw files are read-only. Cleaning in this layer is deliberately limited to safe
source standardisation; business modelling and derived analytics are performed
in SQL so that SQL remains the analytical source of truth.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import re
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path

import pandas as pd


@dataclass(frozen=True)
class SourceSpec:
    file_name: str
    output_name: str
    columns: tuple[str, ...]
    key_columns: tuple[str, ...]
    required_columns: tuple[str, ...]
    numeric_columns: tuple[str, ...] = ()
    datetime_columns: tuple[str, ...] = ()
    lowercase_columns: tuple[str, ...] = ()
    uppercase_columns: tuple[str, ...] = ()
    city_columns: tuple[str, ...] = ()


SPECS = (
    SourceSpec(
        "olist_customers_dataset.csv", "customers.tsv",
        ("customer_id", "customer_unique_id", "customer_zip_code_prefix", "customer_city", "customer_state"),
        ("customer_id",), ("customer_id", "customer_unique_id", "customer_zip_code_prefix"),
        numeric_columns=("customer_zip_code_prefix",), uppercase_columns=("customer_state",), city_columns=("customer_city",),
    ),
    SourceSpec(
        "olist_geolocation_dataset.csv", "geolocation.tsv",
        ("geolocation_zip_code_prefix", "geolocation_lat", "geolocation_lng", "geolocation_city", "geolocation_state"),
        (), ("geolocation_zip_code_prefix", "geolocation_lat", "geolocation_lng"),
        numeric_columns=("geolocation_zip_code_prefix", "geolocation_lat", "geolocation_lng"),
        uppercase_columns=("geolocation_state",), city_columns=("geolocation_city",),
    ),
    SourceSpec(
        "olist_order_items_dataset.csv", "order_items.tsv",
        ("order_id", "order_item_id", "product_id", "seller_id", "shipping_limit_date", "price", "freight_value"),
        ("order_id", "order_item_id"), ("order_id", "order_item_id", "product_id", "seller_id", "price", "freight_value"),
        numeric_columns=("order_item_id", "price", "freight_value"), datetime_columns=("shipping_limit_date",),
    ),
    SourceSpec(
        "olist_order_payments_dataset.csv", "payments.tsv",
        ("order_id", "payment_sequential", "payment_type", "payment_installments", "payment_value"),
        ("order_id", "payment_sequential"), ("order_id", "payment_sequential", "payment_type", "payment_value"),
        numeric_columns=("payment_sequential", "payment_installments", "payment_value"), lowercase_columns=("payment_type",),
    ),
    SourceSpec(
        "olist_order_reviews_dataset.csv", "reviews.tsv",
        ("review_id", "order_id", "review_score", "review_comment_title", "review_comment_message", "review_creation_date", "review_answer_timestamp"),
        ("review_id", "order_id"), ("review_id", "order_id", "review_score"),
        numeric_columns=("review_score",), datetime_columns=("review_creation_date", "review_answer_timestamp"),
    ),
    SourceSpec(
        "olist_orders_dataset.csv", "orders.tsv",
        ("order_id", "customer_id", "order_status", "order_purchase_timestamp", "order_approved_at", "order_delivered_carrier_date", "order_delivered_customer_date", "order_estimated_delivery_date"),
        ("order_id",), ("order_id", "customer_id", "order_status", "order_purchase_timestamp"),
        datetime_columns=("order_purchase_timestamp", "order_approved_at", "order_delivered_carrier_date", "order_delivered_customer_date", "order_estimated_delivery_date"),
        lowercase_columns=("order_status",),
    ),
    SourceSpec(
        "olist_products_dataset.csv", "products.tsv",
        ("product_id", "product_category_name", "product_name_lenght", "product_description_lenght", "product_photos_qty", "product_weight_g", "product_length_cm", "product_height_cm", "product_width_cm"),
        ("product_id",), ("product_id",),
        numeric_columns=("product_name_lenght", "product_description_lenght", "product_photos_qty", "product_weight_g", "product_length_cm", "product_height_cm", "product_width_cm"),
        lowercase_columns=("product_category_name",),
    ),
    SourceSpec(
        "olist_sellers_dataset.csv", "sellers.tsv",
        ("seller_id", "seller_zip_code_prefix", "seller_city", "seller_state"),
        ("seller_id",), ("seller_id", "seller_zip_code_prefix"),
        numeric_columns=("seller_zip_code_prefix",), uppercase_columns=("seller_state",), city_columns=("seller_city",),
    ),
    SourceSpec(
        "product_category_name_translation.csv", "category_translation.tsv",
        ("product_category_name", "product_category_name_english"),
        ("product_category_name",), ("product_category_name", "product_category_name_english"),
        lowercase_columns=("product_category_name", "product_category_name_english"),
    ),
)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def clean_text(value: object) -> str:
    if value is None or pd.isna(value):
        return ""
    return re.sub(r"\s+", " ", str(value)).strip()


def prepare(spec: SourceSpec, raw_dir: Path, processed_dir: Path) -> tuple[dict, list[dict]]:
    source = raw_dir / spec.file_name
    if not source.exists():
        raise FileNotFoundError(f"Required source file not found: {source}")

    frame = pd.read_csv(source, dtype=str, keep_default_na=False, encoding="utf-8")
    missing = [column for column in spec.columns if column not in frame.columns]
    unexpected = [column for column in frame.columns if column not in spec.columns]
    if missing or unexpected:
        raise ValueError(f"Schema mismatch for {spec.file_name}; missing={missing}, unexpected={unexpected}")
    frame = frame.loc[:, spec.columns].copy()

    for column in frame.columns:
        frame[column] = frame[column].map(clean_text)
    for column in spec.lowercase_columns:
        frame[column] = frame[column].str.lower()
    for column in spec.uppercase_columns:
        frame[column] = frame[column].str.upper()
    for column in spec.city_columns:
        frame[column] = frame[column].str.lower()

    checks: list[dict] = []
    for column in spec.required_columns:
        checks.append({
            "source_file": spec.file_name,
            "check_name": f"blank_required:{column}",
            "severity": "error",
            "failed_rows": int(frame[column].eq("").sum()),
        })
    if spec.key_columns:
        checks.append({
            "source_file": spec.file_name,
            "check_name": "duplicate_key:" + "+".join(spec.key_columns),
            "severity": "error",
            "failed_rows": int(frame.duplicated(list(spec.key_columns), keep=False).sum()),
        })
    for column in spec.numeric_columns:
        invalid = frame[column].ne("") & pd.to_numeric(frame[column], errors="coerce").isna()
        checks.append({"source_file": spec.file_name, "check_name": f"invalid_numeric:{column}", "severity": "error", "failed_rows": int(invalid.sum())})
    for column in spec.datetime_columns:
        invalid = frame[column].ne("") & pd.to_datetime(frame[column], errors="coerce").isna()
        checks.append({"source_file": spec.file_name, "check_name": f"invalid_datetime:{column}", "severity": "error", "failed_rows": int(invalid.sum())})

    output = processed_dir / spec.output_name
    frame.to_csv(
        output,
        sep="\t",
        index=False,
        header=False,
        encoding="utf-8",
        lineterminator="\n",
        quoting=csv.QUOTE_NONE,
        escapechar="\\",
    )
    manifest = {
        "source_file": spec.file_name,
        "processed_file": spec.output_name,
        "row_count": int(len(frame)),
        "column_count": int(len(frame.columns)),
        "source_sha256": sha256(source),
        "source_size_bytes": source.stat().st_size,
    }
    return manifest, checks


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-dir", type=Path, required=True)
    parser.add_argument("--processed-dir", type=Path, required=True)
    args = parser.parse_args()
    raw_dir = args.raw_dir.resolve()
    processed_dir = args.processed_dir.resolve()
    processed_dir.mkdir(parents=True, exist_ok=True)

    manifests: list[dict] = []
    checks: list[dict] = []
    for spec in SPECS:
        manifest, file_checks = prepare(spec, raw_dir, processed_dir)
        manifests.append(manifest)
        checks.extend(file_checks)

    profile = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "raw_directory": str(raw_dir),
        "files": manifests,
        "checks": checks,
        "failed_error_checks": sum(1 for check in checks if check["severity"] == "error" and check["failed_rows"] > 0),
    }
    (processed_dir / "manifest.json").write_text(json.dumps(profile, indent=2), encoding="utf-8")
    pd.DataFrame(checks).to_csv(processed_dir / "data_quality_profile.csv", index=False)
    print(json.dumps({"files": len(manifests), "rows": sum(item["row_count"] for item in manifests), "failed_error_checks": profile["failed_error_checks"]}))


if __name__ == "__main__":
    main()

