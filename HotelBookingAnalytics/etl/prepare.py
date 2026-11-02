"""Validate the untouched hotel source and write a deterministic SQL staging file."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

import pandas as pd

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "data/raw/hotel_bookings.csv"
OUT = ROOT / "data/processed"
OUT.mkdir(parents=True, exist_ok=True)

df = pd.read_csv(SOURCE, dtype=str, keep_default_na=False, encoding="utf-8")
expected = [
    "hotel", "is_canceled", "lead_time", "arrival_date_year", "arrival_date_month",
    "arrival_date_week_number", "arrival_date_day_of_month", "stays_in_weekend_nights",
    "stays_in_week_nights", "adults", "children", "babies", "meal", "country",
    "market_segment", "distribution_channel", "is_repeated_guest", "previous_cancellations",
    "previous_bookings_not_canceled", "reserved_room_type", "assigned_room_type",
    "booking_changes", "deposit_type", "agent", "company", "days_in_waiting_list",
    "customer_type", "adr", "required_car_parking_spaces", "total_of_special_requests",
    "reservation_status", "reservation_status_date",
]
if list(df.columns) != expected:
    raise ValueError(f"Unexpected source schema: {list(df.columns)}")
if df.empty:
    raise ValueError("Hotel source is empty")
missing_tokens = {"children": ["NA", ""], "country": ["NULL", ""],
                  "agent": ["NULL", ""], "company": ["NULL", ""]}
missing_counts = {c: int(df[c].isin(tokens).sum()) for c, tokens in missing_tokens.items()}
for col, tokens in missing_tokens.items():
    df.loc[df[col].isin(tokens), col] = ""

numeric = ["is_canceled", "lead_time", "arrival_date_year", "arrival_date_week_number",
           "arrival_date_day_of_month", "stays_in_weekend_nights", "stays_in_week_nights",
           "adults", "children", "babies", "is_repeated_guest", "previous_cancellations",
           "previous_bookings_not_canceled", "booking_changes", "agent", "company",
           "days_in_waiting_list", "adr", "required_car_parking_spaces", "total_of_special_requests"]
invalid_numeric = {c: int((pd.to_numeric(df[c], errors="coerce").isna() & df[c].ne("")).sum()) for c in numeric}
if any(invalid_numeric.values()):
    raise ValueError(f"Non-numeric source values: {invalid_numeric}")
bad_control = {c: int(df[c].str.contains(r"[\t\r\n]", regex=True).sum()) for c in expected}
if any(bad_control.values()):
    raise ValueError(f"Tabs or newlines in source values: {bad_control}")

staging = df.copy()
staging.insert(0, "source_row", range(2, len(df) + 2))
staging.to_csv(OUT / "hotel_staging.tsv", sep="\t", index=False, header=False, lineterminator="\n", na_rep="", quoting=3)
manifest = {
    "source": SOURCE.name,
    "sha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    "rows": len(df),
    "exact_duplicate_rows": int(df.duplicated().sum()),
    "missing_country": missing_counts["country"],
    "missing_children": missing_counts["children"],
    "missing_agent": missing_counts["agent"],
    "missing_company": missing_counts["company"],
    "zero_night_bookings": int((pd.to_numeric(df.stays_in_week_nights) + pd.to_numeric(df.stays_in_weekend_nights)).eq(0).sum()),
    "negative_adr": int(pd.to_numeric(df.adr).lt(0).sum()),
}
(OUT / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print(json.dumps(manifest, indent=2))
