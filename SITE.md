# Portfolio website

Canonical URL: **https://mishagumberidze-del.github.io/Portfolio/**

This is a buildless static site served by GitHub Pages from the root of `main`. The repository is public for GitHub Free Pages hosting. The `.nojekyll` file disables Jekyll processing; no npm build, server, database, custom domain or paid hosting is required for the landing page.

## Editing

- `index.html`: landing-page copy, project sections, report links and metadata.
- `assets/portfolio.css`: responsive layout and visual styling.
- `assets/portfolio.js`: five case studies, report galleries, dialog interactions and keyboard focus handling.
- `assets/*.png`: pre-audit report previews and the earlier synthetic Spotify demo preview. Current NYC and Retail screenshots are in `NYCTaxiAnalytics/site/qa/reviewed-*.jpg` and `OnlineRetailPython/report/qa/reviewed-*.jpg`. Current fictional Spotify overview previews are in `SpotifyTastePrototype/docs/qa/reviewed-demo-*.jpg`. Refreshed Hotel Power BI previews are in `HotelBookingAnalytics/docs/qa/reviewed-*-2026-11-05.png`; Olist previews are in `OlistEcommerce/docs/qa/reviewed-*-2026-11-06.png`.
- `NYCTaxiAnalytics/site/index.html` and `OnlineRetailPython/report/online_retail_analysis.html`: existing self-contained report artifacts, linked directly from the website.

Keep local links relative so they work under the `/Portfolio/` project-site path. Edit locally, verify the previews and links, then commit/push to `main`; GitHub Pages republishes the current branch. Power BI source files remain in their project folders for technical review.

## Preview and checks

Run a static HTTP server from the repository folder, for example:

```powershell
python -m http.server 8000 --bind 127.0.0.1
```

Open `http://127.0.0.1:8000/`. In a second terminal, run `python tests/check_site.py`. This creates an ignored `qa-portfolio.html` page. Its browser test checks five case studies, 13 gallery images, the two local report links, dialog close/focus behavior and page overflow. Successful results appear on the document element as `data-portfolio-qa` with `status: pass`.

The earlier site version was checked on desktop and narrow layouts and at the `/Portfolio/` path. Current static path checks are separate from a new rendered browser review. A 404 page directs visitors back to the portfolio. Reduced-motion preferences are respected; case-study dialogs support a close button, Escape and backdrop clicks.

## Publication scope

The public site uses only the audited repository content. No raw datasets, private Spotify payloads, credentials, analytics tracker, contact form or paid API is required. The separately hosted Spotify link remains its existing read-only edition; the linked Spotify screenshot shows the current synthetic overview. Browser interaction checks cover the local synthetic edition. The deployed read-only production edition separately passed ten route checks, current HTML/content-security-policy checks and asset/snapshot integrity checks; the private snapshot is not stored here.
