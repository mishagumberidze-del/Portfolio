# Verification and starter-release scope

Project QA notes retain the substantive checks performed during development: source/core row reconciliation, Power BI Desktop refresh/review, statistical metric boundaries, and Taxi browser/state checks. The starter pack additionally receives a publication audit of staged files and links.

For this release, the copied Spotify solution restored/built successfully and all 143 backend tests and 18 frontend helper tests passed. Its synthetic frontend passed TypeScript compilation and a Vite production build; the built page returned HTTP 200 and was rendered for the included screenshot. The existing hosted Spotify landing page also returned HTTP 200. No local listening database or personal snapshot was needed for these release checks.

The portfolio landing page was reviewed at desktop and narrow sizes. Browser checks passed at 1,422 px, 504 px and a true 360 px frame: all five case-study dialogs, 13 gallery images, two local report links, close/focus behavior and zero page-level horizontal overflow. Local paths were checked under the `/Portfolio/` project-site prefix. Website source and repeatable checks are documented in [SITE.md](SITE.md).

## What is included

- Five project source packages with SQL, Python, Power BI PBIP/TMDL/PBIR, or React/.NET code as appropriate.
- Downloadable, self-contained Taxi and Retail HTML artifacts containing aggregate public-data results.
- Report previews, architecture/metric documentation, data attribution and reproducible setup commands.
- Spotify synthetic demo data and automated source tests; no private account payload.

## What is excluded

Raw datasets and exports, personal snapshots, account credentials/tokens, local connection overrides, database files, dependency folders, runtime caches, backup/trash folders and internal session handoff notes.

Power BI source definitions are included without local caches or source CSVs. They require the documented dataset/pipeline setup before refresh. Existing project QA is evidence from the development environment; it does not imply an automatic clean-machine end-to-end build. GitHub Pages publication is a separate static deployment of the audited repository.
