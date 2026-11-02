"""Validate Latin-1 retail CSV, preserving every source line for SQL staging."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import pandas as pd

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "data/raw/online_retail_II.csv"
OUT = ROOT / "data/processed"
OUT.mkdir(parents=True, exist_ok=True)
df = pd.read_csv(SOURCE, encoding="latin1", dtype=str, keep_default_na=False)
expected = ["Invoice", "StockCode", "Description", "Quantity", "InvoiceDate", "Price", "Customer ID", "Country"]
if list(df.columns) != expected or df.empty:
    raise ValueError(f"Unexpected or empty Retail source: {list(df.columns)}")
for col in expected:
    if df[col].str.contains(r"[\t\r\n]", regex=True).any():
        raise ValueError(f"Control character in {col}")
dates = pd.to_datetime(df.InvoiceDate, format="%m/%d/%y %H:%M", errors="coerce")
if dates.isna().any():
    raise ValueError(f"{dates.isna().sum()} invalid timestamps")
for col in ["Quantity", "Price"]:
    if pd.to_numeric(df[col], errors="coerce").isna().any():
        raise ValueError(f"Invalid numeric values in {col}")
if df.Invoice.eq("").any() or df.StockCode.eq("").any():
    raise ValueError("Missing invoice or stock code")
df.insert(0, "source_row", range(2, len(df) + 2))
df.to_csv(OUT / "retail_staging.tsv", sep="\t", index=False, header=False, lineterminator="\n", quoting=3)
qty = pd.to_numeric(df.Quantity)
price = pd.to_numeric(df.Price)
manifest = {
    "source": SOURCE.name,
    "sha256": hashlib.sha256(SOURCE.read_bytes()).hexdigest(),
    "rows": len(df),
    "exact_duplicate_rows": int(df[expected].duplicated().sum()),
    "missing_customer_rows": int(df["Customer ID"].eq("").sum()),
    "missing_description_rows": int(df.Description.eq("").sum()),
    "cancellation_prefix_rows": int(df.Invoice.str.startswith("C").sum()),
    "negative_quantity_rows": int(qty.lt(0).sum()),
    "negative_quantity_without_c_prefix": int((qty.lt(0) & ~df.Invoice.str.startswith("C")).sum()),
    "zero_price_rows": int(price.eq(0).sum()),
    "negative_price_rows": int(price.lt(0).sum()),
    "invoices_with_multiple_timestamps": int((df.groupby("Invoice").InvoiceDate.nunique() > 1).sum()),
    "date_min": str(dates.min()), "date_max": str(dates.max()),
}
(OUT / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print(json.dumps(manifest, indent=2))
