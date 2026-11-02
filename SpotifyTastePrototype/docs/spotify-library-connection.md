# Optional read-only Spotify library connection

The API supports an optional local Liked Songs import using OAuth Authorization Code with PKCE. The repository contains the implementation, not credentials or an imported library.

## Design

- PKCE S256, random state, a short-lived pending authorization and a browser-binding cookie.
- A fixed loopback callback and return destination; no provider-controlled redirects.
- Access/refresh tokens live only in API process memory and are dropped on restart.
- Spotify requests use fixed HTTPS endpoints with redirects disabled.
- Local connection/import routes verify loopback source, local Host, dashboard Origin and a custom request header.
- Read-only application mode disables import and other maintenance mutations.

## Import semantics

The importer paginates the complete saved-track collection, validating totals, offsets, unique IDs, response shape and timestamps. It does not trust provider `next` URLs as arbitrary destinations for bearer tokens. Only a complete validated snapshot enters a serializable database transaction. Imported saved state is separate from listening facts: a saved track is not fabricated as a play. Re-linking and recording equivalence are handled explicitly.

For a local authorized setup, configure only the application's Client ID in ignored `appsettings.Local.json`, register the loopback callback in the provider's developer settings, and personally authorize `user-library-read`. Provider eligibility, quotas and account restrictions apply; consult the current Spotify documentation. No client secret or token should be committed.
