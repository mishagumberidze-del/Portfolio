# SQL model and metric definitions

- `stg.RetailLine`: eight original fields plus unique `SourceRow` from the CSV line number.
- `core.Country` (`CountryId` PK), `core.Customer` (`CustomerId` PK, observed customer ID), `core.Product` (`ProductId` PK, source StockCode retained), and `core.Date` (`DateKey` PK).
- `core.Invoice` (`InvoiceId` PK, source invoice unique): customer and country FKs, earliest timestamp, cancellation-prefix flag, and timestamp-conflict flag. Source invoice numbers have consistent customer and country in this file; 43 have multiple timestamps.
- `core.InvoiceLine` (`InvoiceLineId` PK, `SourceRow` unique): invoice/product/date FKs, original line timestamp and description, quantity, price, amount, and line class.
- `rpt.LineAnalysis`: line-level gross, return/adjustment, and net value. `rpt.CustomerValue`: identified-customer invoice frequency, first/last purchase, gross, returns, and net.
- `etl.LoadAudit`: source hash, source/core counts, duplicate count, missing-customer count, and load time.

Positive quantity and positive price lines are `SALE`. Negative quantity with positive price is `RETURN_OR_ADJUST`, whether or not the invoice starts with `C`. Zero and negative prices are retained but excluded from value metrics. Exact duplicate rows are preserved because the source has no line ID. Anonymous lines remain unassigned to a customer.

Gross, return, and net values are based on quantity × price; they are transaction-line values, not audited revenue or profit. Source currency is not explicitly encoded in the CSV, so the report uses **CU** (currency units). Service codes such as postage remain in overall transaction value but are excluded from the merchandise-coded stock ranking using a leading-digit rule.

The concentration view ranks identified positive-sale purchasers by gross line value, with customer ID breaking ties. The top decile contains the ceiling of 10% of those customers. Both groups show gross, credits and net value; return-only customers are disclosed separately. No arbitrary loyalty or risk labels are inferred. Monthly cohorts start at each identified customer's first observed positive-sale month; later cells use distinct customers with a positive-sale invoice and blank out months the cohort has not yet reached, as well as the incomplete final month.

Unit prices and line amounts retain four decimal places. Four source prices are 0.001; rounding them to cents changes their classification. The source/core amount reconciliation prevents this loss of precision.

The 90-day repeat view includes only customers whose first observed purchase is at least 90 days before the last observed date. It checks the second distinct sale invoice within 90 days. Quarter summaries show the eligible customer count, repeated customer count and their observed ratio. They describe this extract and do not establish causal effects. First observed purchase is not necessarily lifetime acquisition.

The headline uses the pooled repeated/eligible customer counts across these cohorts, not an average of quarter percentages and not the unequal-follow-up full-window repeat rate. It is 1,607 / 3,370 = 47.69%. The same-day second-invoice rule and customer-ID coverage still apply. See [KPI usefulness](kpi_usefulness.md) for the question behind each displayed measure.

SQL product identity is case-insensitive and ignores trailing spaces. The independent source reconciliation normalizes only this identity comparison and BCP empty-string NUL bytes; dates, quantities and monetary values remain exact.
