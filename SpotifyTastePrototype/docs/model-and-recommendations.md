# Preference modeling and recommendations

## Evidence and identity

The implementation separates immutable listening events, catalog releases, conservative recording identity, cached audio features, derived preference rows and recommendation/evaluation runs. Equivalent recordings can influence exclusions without rewriting actual release IDs or saved-state facts.

## Preference signals

The scoring/model code uses qualified-play thresholds, recent and lifetime behavior, time decay, artist familiarity, saved-state evidence and available audio features. Scores are explainable and versioned. Missing audio-feature coverage is visible rather than filled with invented feature values. Missing scoring components are shown as unavailable rather than as neutral numeric results. Validation handles tied evidence and absent comparable neighbors explicitly.

Inspect [PreferenceModel](../backend/SpotifyTaste.Application/PreferenceModel.cs), [RecordingIdentity](../backend/SpotifyTaste.Application/RecordingIdentity.cs), application contracts, and infrastructure implementations for the executable definitions. Configuration in the API includes qualified-play duration/fraction, decay and saved/skip weights.

## Discovery and evaluation

Recording-aware exclusions prevent already-known equivalents from being presented as new discoveries. Diversity logic limits concentration and provides a more varied candidate set. Evaluation runs and feedback are represented separately from listening history and are not fabricated as human judgments. Recording-held-out validation reduces leakage from equivalent releases.

Inspect [RecommendationDiversity](../backend/SpotifyTaste.Application/RecommendationDiversity.cs), [feedback contracts](../backend/SpotifyTaste.Application/FeedbackContracts.cs) and [automated tests](../backend/SpotifyTaste.Tests).

The included demo derives its analytics from coherent fictional events. Model validation, preference scores, audio features, recommendations and human evaluation are unavailable in this fixture. No fitted results are fabricated. The read-only edition preserves existing results and restricts regeneration/maintenance operations explicitly.
