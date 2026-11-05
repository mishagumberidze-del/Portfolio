# Verified findings

All figures are computed from SQL-produced `rpt` exports in the [HTML report](../report/online_retail_analysis.html).

- 541,910 source lines in 25,900 invoices; 4,372 observed customer IDs, of which 4,338 have a positive-sale invoice.
- Gross positive line value is 10,666,702.54 CU; returns and negative-quantity adjustments are 896,812.49 CU; net is 9,769,890.05 CU.
- 1,607 of 3,370 identified customers with a complete 90-day follow-up window make a second positive-sale invoice within 90 days: 47.69%. This is the headline repeat metric; customers with shorter follow-up are excluded.
- The top 10% of identified purchasing customers account for 61.37% of their gross positive line value.
- The United Kingdom accounts for 84.61% of gross positive line value. November 2011 has the highest monthly net value, but December 2011 is partial.
- Stock-level descriptions are noisy and some stock codes represent services. Product rankings are indicative, not a clean catalog profitability analysis.
- Identified customers represent 83.54% of gross positive line value. Median identified positive-sale invoice value is 303.04 CU versus mean 480.87 CU, indicating a skewed distribution.
- Under a fixed 90-day follow-up rule, first-observed-purchase cohorts show repeat rates of 59.5% (2010Q4), 44.8% (2011Q1), 39.8% (2011Q2), and 46.6% (eligible part of 2011Q3). The first cohort may contain existing customers; these differences do not measure campaign effects.
