# Verified findings

- All 48 source files contributed 298,930,712 records; 298,930,636 pickups fall in 2024 and 76 fall outside it.
- Counts below are source submissions, not a count of all real-world transport activity.

| Service | 2024 pickup records | Mean eligible duration |
|---|---:|---:|
| Yellow taxi | 41,169,664 | 16.78 min |
| Green taxi | 660,198 | 14.66 min |
| FHV | 17,630,326 | 24.36 min |
| High-volume FHV | 239,470,448 | 20.13 min |

- The highest monthly pickup count is 2024-12: 26,704,382. These are monthly totals, not day-normalized demand.
- The busiest pickup hour across all services begins at 18:00 NYC local clock time.
- The leading mapped pickup zone is JFK Airport with 6,361,883 records.
- 95.25% of in-year pickups map to one of the 263 physical zones. Unknown/outside-NYC IDs remain in overall totals.

## Actions to evaluate

- Compare hourly patterns within each service and normalize weekday demand per calendar day before staffing or dispatch decisions.
- Investigate unknown zone coverage by source population before interpreting geography or recommending service changes.
- Use specific airport and neighborhood pairs for operational review; inspect both trip counts and service definitions rather than treating all fare concepts as interchangeable.
- Validate proposed scheduling or service interventions using later periods or a controlled operational trial. These descriptive results do not establish causal effects.
