# Music stories and evaluation

## Story layer

The frontend presents lifetime/year stories, top tracks/artists/albums, listening-time comparisons, behavior patterns and artist-rank changes. Story totals and units are formatted consistently; missing coverage and unavailable actions are surfaced in the interface.

Relevant files include [MusicStory.tsx](../frontend/spotify-taste-web/src/MusicStory.tsx), [story-math.ts](../frontend/spotify-taste-web/src/story-math.ts), [ArtistTimeline.tsx](../frontend/spotify-taste-web/src/ArtistTimeline.tsx), and [number-format.ts](../frontend/spotify-taste-web/src/number-format.ts).

## Evaluation boundaries

The backend represents recommendation sessions, candidates, feedback and evaluation items separately. Automated validation and stored metrics do not stand in for real user feedback. Recording-held-out checks address an important source of catalog leakage, but do not establish general recommendation quality for unrelated users.

The repository's synthetic mode is a UI demonstration. Private account exports and snapshot payloads are excluded. The API-backed edition requires an authorized local data source; maintenance and write operations remain restricted by explicit application policy.
