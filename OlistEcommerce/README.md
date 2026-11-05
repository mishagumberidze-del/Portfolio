# Olist Customer & E-Commerce Performance

SQL Server + Python + Power BI analysis of customer value, repeat purchasing, products, sellers and delivery experience.

![Power BI overview](powerbi/qa/Executive%20Overview.png)

> Pre-audit visual preview. See the current source and metric documentation for the reviewed calculations.

## What this demonstrates

- Nine source tables and an explicit distinction between order-specific `customer_id` and enduring `customer_unique_id`.
- Repeatable ETL, source hashing, rejected-record capture, relational constraints, quality checks and analytical SQL views.
- Five Power BI report pages plus customer drill-through, 47 DAX measures, six executive KPIs and purchase cohorts, dynamic trend selection and Top-N controls.

The observed source has 99,441 orders and 96,096 enduring customer identities. The commercial population excludes canceled/unavailable orders: 98,207 orders and 94,990 purchasing customers; 2,888 repeat customers give an observed-window repeat rate of 3.04%. Findings are descriptive and account for partial boundary periods.

## Review the implementation

- [Data model](docs/data_model.md) · [KPI definitions](docs/kpi_definitions.md) · [KPI usefulness](docs/kpi_usefulness.md) · [Findings](docs/findings.md) · [Data quality](docs/data_quality.md)
- [SQL transformations](sql/03_transform_core.sql) · [Analytical SQL](sql/06_portfolio_analysis.sql) · [DAX](powerbi/measures.dax)
- [PBIP project](powerbi/OlistEcommercePBIP/OlistEcommerce.pbip) · [Power BI guide](powerbi/README.md)

## Rebuild

Obtain the [Olist source](https://www.kaggle.com/datasets/olistbr/brazilian-ecommerce) and place its nine CSV files in `data/raw/`. Install Python dependencies from `requirements.txt`; SQL Server Express, Windows authentication and Microsoft `sqlcmd`/`bcp` are required.

```powershell
python -m pip install -r requirements.txt
& .\etl\run_pipeline.ps1 -PythonPath python
```

Open the PBIP in Power BI Desktop, set `DataRoot` to this checkout's `data/processed/powerbi`, then refresh. Database: `Portfolio_Olist` on `.\SQLEXPRESS`. Raw data and Power BI caches are not included in this starter repository. The SQL assertions are in `tests/assert_model.sql`.
