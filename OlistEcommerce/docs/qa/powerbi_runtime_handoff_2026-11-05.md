# Power BI runtime handoff — 2026-11-05

Historical record, superseded by the [successful native refresh and interaction checks on 2026-11-06](powerbi_native_validation_2026-11-06.md).

Power BI Desktop 2.159.1231.0 launched successfully, but the Hotel project needed a circular-sort dependency fix before the planned Olist pass. The user requested handoff before the remaining runtime checks were completed. Olist was not opened, refreshed or evaluated in the native engine during this pass. No new report screenshots were saved.

The previous source/export checks remain recorded separately; they are not native rendering or DAX evidence. To finish manually, open `powerbi/OlistEcommercePBIP/OlistEcommerce.pbip`, refresh, and inspect all six pages and their filters. With filters cleared, the executive page should show 13,494,400.74 BRL GMV, 98,207 sales orders, 137.41 BRL average order value, 0.63% cancellation, 93.23% on-time delivery and 4.09 average review score. On-time delivery uses 89,936 / 96,470 evaluable delivered orders. Existing screenshots predate the calculation audit.
