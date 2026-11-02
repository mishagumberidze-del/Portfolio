# Database schema

The schema was finalized after inspecting the actual Extended Streaming History package. It separates immutable facts, music identity, provider data, real library state, and derived results.

## Core relationships

```text
User ──< ListeningEvent >── Track ──< TrackArtist >── Artist
                                └── Album ──> PrimaryArtist
                                ├──< TrackExternalId
                                ├──< TrackFeature
                                ├──< TrackPreference >── User
                                └──< UserSavedTrack >── User

ImportBatch ──< ImportError
RecommendationRun ──< RecommendationCandidate >── Track
RecommendationCandidate ──< RecommendationFeedback >── EvaluationItem
EvaluationSession ──< EvaluationItem >── RecommendationCandidate
Track ──< TrackGenre >── Genre
```

## Fact and library state

- `ListeningEvent`: user/track foreign keys, `PlayedAtUtc`, `MsPlayed`, nullable skip/shuffle/offline/private flags, bounded platform/start/end strings, source, import timestamp, and unique 64-character event hash. It contains no duplicated artist, album, artwork, genre, or feature fields.
- `UserSavedTrack`: real exported saved state only. `SavedAtUtc` remains nullable. It is empty for the current package because no recognized library dataset exists.
- `User`: one local record. There is no authentication or multi-user UI.

## Music identity

- `Track`: unique filtered Spotify ID and URI indexes, optional ISRC/album/duration/release date/link, and enrichment status.
- `Artist`: optional filtered-unique Spotify ID plus conservative normalized-name lookup. Artist names are not fuzzy-merged.
- `Album`: optional filtered-unique Spotify ID, name, primary artist, release fields, and artwork URL.
- `TrackArtist`: composite identity and ordered collaborators.
- `Genre` / `TrackGenre`: normalized taxonomy with an explicit source.

Physical `Track` identity remains the exact Spotify ID/URI so source facts and provider metadata are never destructively merged. A separate derived recording resolver groups analytical/scoring views using exact Spotify ID, guarded ISRC, exact normalized title and ordered artists, compatible duration, and—only for incomplete data—exact album. Album packaging words such as Standard, Deluxe, Single, or Compilation do not define a new recording. Explicit live/remix/remaster/acoustic/edit/etc. markers do. Title alone is never sufficient.

The current database contains 12,275 historical Spotify track IDs and 11,720 derived recording clusters. No migration or fact-row rewrite was required; this equivalence layer is recomputed from cached metadata and can be refined safely.

## Provider cache

- `TrackExternalId`: unique `(Provider, ExternalId)` and `(TrackId, Provider)` pairs.
- `TrackFeature`: unique `(TrackId, Provider)` with nullable ReccoBeats values. Only returned fields are populated.
- A track-level status distinguishes pending, completed, not found, retry later, and failed outcomes.

## Audit and derived data

- `ImportBatch` and `ImportError` make counts and rejected rows auditable.
- `TrackPreference` stores positive, negative, recency, repeat, completion, skip, saved, and final behavioral values per model version.
- `RecommendationRun` and `RecommendationCandidate` retain provider/model versions, rank/band, every score component, novelty/exploration, and deterministic explanation.
- `RecommendationCandidate` also retains bounded confidence, album name and artwork URL for restoring cards across refreshes. Older runs have `Unknown` confidence rather than invented values.
- `RecommendationFeedback`: append-only explicit labels with candidate FK, optional evaluation-item FK, optional 1–5 `tinyint` rating, bounded kind and `datetime2` timestamp. It is not a Spotify Like or listening event.
- `EvaluationSession`: creation timestamp and target size constrained to 50–100.
- `EvaluationItem`: stable cohort position and candidate FK; unique position per session and unique candidate membership. Metrics use latest explicit evaluation labels, not model-inferred labels.

Migration `20260905020725_RecommendationFeedbackAndEvaluation` is additive and applied to `SpotifyTaste`. Its four new FKs are all NO ACTION. No history, provider-track or saved-track rows were rewritten. The verified fact total remains 61,110 events / 9,986,416,583 ms.

All timestamps use `datetime2`. Text columns have bounded lengths. Deletes default to `NO ACTION`; only dependent import errors and recommendation candidates cascade from their owning batch/run.
