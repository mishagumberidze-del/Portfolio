# Spotify Taste — Listening Analytics & Discovery

A React/TypeScript application backed by an ASP.NET Core API and SQL Server, combining listening analytics, explainable preference scoring and recording-aware discovery.

![Synthetic demo interface](../assets/spotify-demo.png)

[Hosted read-only portfolio edition](https://spotify-taste-demo.pages.dev/) · The hosted edition uses a separately managed snapshot; this repository contains synthetic demo data, not that snapshot.

## What this demonstrates

- Layered domain/application/infrastructure/API design with EF Core and automated backend tests.
- Idempotent/auditable import of Extended Streaming History, source reconciliation and conservative track/recording identity.
- Interactive charts, yearly/lifetime stories, behavior and audio-feature coverage, and explainable preference signals.
- Diversified recommendations, recording-level exclusions, feedback/evaluation logic and recording-held-out validation.

Stack: **React 19, TypeScript, Vite, TanStack Query, Recharts, ASP.NET Core 8, EF Core, SQL Server and xUnit**.

## Run the synthetic demo

The starter pack contains fictional demo responses and source code. Private streaming exports, liked-song snapshots, OAuth tokens and local settings are excluded.

```powershell
cd frontend/spotify-taste-web
npm ci
npm run build:demo
npm run preview -- --host 127.0.0.1
```

The included `.env.demo` selects synthetic demo mode. It needs no SQL Server, account access or enrichment API. Personal snapshot mode is retained in source but its private payload is not included.

## API-backed edition

Requires the .NET 8 SDK, Node.js and SQL Server. Set an appropriate local connection string for the `SpotifyTaste` database and import only an authorized export. The normal API defaults to read-only; maintenance/import functions require explicit local enablement as described in source.

```powershell
dotnet restore SpotifyTasteSolution.sln
dotnet test SpotifyTasteSolution.sln
dotnet run --project backend/SpotifyTaste.Api
```

The development frontend proxies `/api` to the local API; a normal frontend build remains API-backed. Database initialization and import depend on the chosen local edition/configuration, so a fresh clone does not contain populated listening data.

## Code tour

[Database relationships](docs/database-schema.md) · [Preference model and recommendations](docs/model-and-recommendations.md) · [Stories and evaluation](docs/music-story-and-evaluation.md)

[Domain](backend/SpotifyTaste.Domain) · [Application](backend/SpotifyTaste.Application) · [Infrastructure](backend/SpotifyTaste.Infrastructure) · [API](backend/SpotifyTaste.Api) · [Tests](backend/SpotifyTaste.Tests) · [React source](frontend/spotify-taste-web/src)
