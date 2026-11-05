# Power BI validation — 2026-11-04

Static checks passed for the current project: 3 tables, 10 measures, 3 pages, 27 visuals and 20 distinct report field references. DataRoot points to the local refreshed exports. All imported TSV rows match their declared column counts; all model source columns resolve; the arrival-date relationship has a unique dimension key and no unmatched booking keys. Report references and visual bounds resolve against the current model and page sizes.

Independent aggregation of those exports gives 119,390 bookings, 44,224 cancellations, 74,486 stayed bookings, 255,030 rate-eligible nights, 25,996,324.21 CU room-value proxy and 101.93 CU stay-weighted ADR. City and Resort hotel subsets were also checked. Exact values, file hashes and check scope are in [the machine-readable evidence](powerbi_static_validation_2026-11-04.json).

Power BI Desktop 2.159.883.0 is installed through the Microsoft Store. The attempted native launch returned **“Computer Use app approval timed out”**. No Desktop refresh, DAX execution, displayed headline value or interactive filter behavior was verified in this pass. Existing report screenshots are pre-audit previews; no fresh screenshots were created. Static source checks do not replace native DAX and report-render verification.
