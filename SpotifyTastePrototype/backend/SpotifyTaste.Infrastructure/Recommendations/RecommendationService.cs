using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.Preferences;

namespace SpotifyTaste.Infrastructure.Recommendations;

public sealed class RecommendationService(
    SpotifyTasteDbContext dbContext,
    ICandidateTrackProvider candidateProvider,
    ISongScoringService scoringService,
    ILogger<RecommendationService> logger,
    IOptions<PreferenceModelOptions> configuredOptions) : IRecommendationService
{
    private readonly PreferenceModelOptions options = configuredOptions.Value;
    public async Task<DiscoverDto> GenerateAsync(int countPerList, CancellationToken cancellationToken = default)
    {
        countPerList = Math.Clamp(countPerList, 1, 50);
        var user = await dbContext.Users.SingleAsync(cancellationToken);
        var history = await LoadRecordingCatalogAsync(dbContext.ListeningEvents.AsNoTracking().Select(x => x.TrackId).Distinct(), cancellationToken);
        var saved = await LoadRecordingCatalogAsync(dbContext.UserSavedTracks.AsNoTracking().Select(x => x.TrackId).Distinct(), cancellationToken);
        // Shown recordings stay suppressed across releases and across runs. Feedback is
        // append-only; it does not modify history, library state, or model weights.
        var shown = await LoadRecordingCatalogAsync(dbContext.RecommendationCandidates.AsNoTracking().Select(x => x.TrackId).Distinct(), cancellationToken);
        var candidates = (await candidateProvider.GetCandidatesAsync(Math.Max(200, countPerList * 20), cancellationToken))
            .Where(x => x.SpotifyTrackId is not null)
            .DistinctBy(x => x.SpotifyTrackId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        candidates = DistinctRecordings(candidates).Where(x => !shown.Contains(x)).ToArray();

        if (scoringService is SongScoringService concreteScorer)
            await concreteScorer.PrimeTrackCacheAsync(candidates.Select(x => x.SpotifyTrackId!), cancellationToken);

        var scored = await ScoreCandidatesAsync(candidates, cancellationToken);
        // Failed candidate work may have cleared the scoped change tracker.
        // Reload the existing local user before attaching newly persisted runs.
        user = await dbContext.Users.SingleAsync(cancellationToken);
        var neverHeard = SelectMix(scored.Where(x => !history.Contains(x.External)), countPerList);
        var notSaved = user.SpotifyLibraryDataAvailable ? SelectMix(scored.Where(x => !saved.Contains(x.External)), countPerList) : [];

        var neverDtos = await StoreRunAsync(user, RecommendationListType.NeverHeard, neverHeard, history, saved, cancellationToken);
        IReadOnlyList<RecommendationDto> notSavedDtos = [];
        if (user.SpotifyLibraryDataAvailable) notSavedDtos = await StoreRunAsync(user, RecommendationListType.NotSaved, notSaved, history, saved, cancellationToken);
        return new DiscoverDto(user.SpotifyLibraryDataAvailable, user.SpotifyLibraryDataAvailable ? null : "Spotify saved/liked library data was not found in the imported package.", notSavedDtos, neverDtos);
    }

    private async Task<List<ScoredCandidate>> ScoreCandidatesAsync(IReadOnlyList<ExternalTrack> candidates, CancellationToken cancellationToken)
    {
        // The scorer and this service share a scoped EF DbContext. Keep database-backed
        // scoring sequential; the enrichment provider separately controls HTTP concurrency.
        var scored = new List<ScoredCandidate>();
        foreach (var candidate in candidates)
        {
            try
            {
                var score = candidate.Features is not null && scoringService is SongScoringService concrete
                    ? await concrete.ScoreExternalAsync(candidate, cancellationToken)
                    : await scoringService.ScoreAsync(candidate.SpotifyUrl ?? candidate.SpotifyTrackId!, cancellationToken);
                scored.Add(new ScoredCandidate(candidate, score));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // SaveChanges is atomic, but a failed attach/save leaves pending
                // entities in EF. Never carry that failed graph into another song.
                dbContext.ChangeTracker.Clear();
                logger.LogWarning(exception, "Candidate {SpotifyTrackId} could not be scored and was skipped.", candidate.SpotifyTrackId);
            }
        }
        return scored;
    }

    private static IReadOnlyList<(ScoredCandidate Candidate, RecommendationBand Band)> SelectMix(IEnumerable<ScoredCandidate> source, int count)
    {
        var all = source.OrderByDescending(x => x.Score.PredictedLikeScore).ToList();
        var selected = new List<(ScoredCandidate, RecommendationBand)>();
        var artists = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var highCount = (int)Math.Ceiling(count * 0.5);
        var discoveryCount = (int)Math.Floor(count * 0.3);
        Add(all.Where(x => x.Score.Components.FeatureMatch >= 6.5), RecommendationBand.HighConfidence, highCount);
        Add(all.Where(x => x.Score.Components.FeatureMatch is >= 4.8 and < 7.5), RecommendationBand.Discovery, discoveryCount);
        Add(all.Where(x => x.Score.Components.FeatureMatch is >= 3.0 and < 6.5 && x.Score.PredictedLikeScore >= 4.5), RecommendationBand.Wildcard, count - selected.Count);
        Add(all, RecommendationBand.Discovery, count - selected.Count);
        return selected;

        void Add(IEnumerable<ScoredCandidate> pool, RecommendationBand band, int desired)
        {
            if (desired <= 0 || selected.Count >= count) return;
            var added = 0;
            foreach (var item in pool)
            {
                var artist = item.External.Artists.FirstOrDefault() ?? "Unknown Artist";
                if (selected.Any(existing => Equivalent(existing.Item1.External, item.External)) || artists.Contains(artist)) continue;
                artists.Add(artist); selected.Add((item, band)); added++;
                if (added >= desired || selected.Count >= count) break;
            }
        }
    }

    private async Task<IReadOnlyList<RecommendationDto>> StoreRunAsync(User user, RecommendationListType listType, IReadOnlyList<(ScoredCandidate Candidate, RecommendationBand Band)> selected, RecordingCatalog history, RecordingCatalog saved, CancellationToken cancellationToken)
    {
        var run = new RecommendationRun { User = user, ListType = listType, ModelVersion = options.ModelVersion, CandidateProvider = candidateProvider.Name, CreatedAtUtc = DateTime.UtcNow };
        dbContext.RecommendationRuns.Add(run);
        var dtos = new List<RecommendationDto>();
        var rank = 0;
        foreach (var (item, band) in selected)
        {
            rank++;
            var track = await GetOrCreateCandidateTrackAsync(item.External, cancellationToken);
            var explanation = string.Join(" ", item.Score.Explanations);
            run.Candidates.Add(new RecommendationCandidate
            {
                Confidence = item.Score.Confidence, ArtworkUrl = item.External.ImageUrl, AlbumName = item.External.Album,
                RecommendationRun = run, Track = track, Rank = (short)rank, Band = band,
                FinalScore = (decimal)item.Score.PredictedLikeScore,
                AudioFeatureSimilarity = (decimal)item.Score.Components.FeatureMatch,
                RecentTasteSimilarity = (decimal)item.Score.Components.RecentTasteMatch,
                LifetimeTasteSimilarity = (decimal)item.Score.Components.LifetimeTasteMatch,
                ArtistAffinity = (decimal)item.Score.Components.ArtistTasteMatch,
                HistoricalBehaviorSimilarity = (decimal)item.Score.Components.HistoricalBehaviorMatch,
                SavedTrackSimilarity = (decimal)item.Score.Components.SavedTrackMatch,
                NoveltyScore = history.Contains(item.External) ? 0 : 10,
                ExplorationScore = (decimal)item.Score.Components.ExplorationAdjustment,
                Explanation = explanation,
            });
            var status = saved.Contains(item.External) ? "Saved" : history.Contains(item.External) ? "Heard But Not Saved" : "Never Heard";
            dtos.Add(new RecommendationDto(rank, item.External.Name, item.External.Artists.FirstOrDefault() ?? "Unknown Artist", item.External.ImageUrl, item.Score.PredictedLikeScore, item.Score.Confidence, explanation, status, item.External.SpotifyUrl, band.ToString()));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return dtos.Select(dto => dto with { CandidateId = run.Candidates.Single(c => c.Rank == dto.Rank).Id, Album = run.Candidates.Single(c => c.Rank == dto.Rank).AlbumName }).ToArray();
    }

    private async Task<Track> GetOrCreateCandidateTrackAsync(ExternalTrack candidate, CancellationToken cancellationToken)
    {
        var existing = await dbContext.Tracks.Include(x => x.TrackArtists).SingleOrDefaultAsync(x => x.SpotifyTrackId == candidate.SpotifyTrackId, cancellationToken);
        if (existing is not null) return existing;
        var now = DateTime.UtcNow;
        var track = new Track
        {
            SpotifyTrackId = candidate.SpotifyTrackId, SpotifyUri = $"spotify:track:{candidate.SpotifyTrackId}", SpotifyUrl = candidate.SpotifyUrl,
            Isrc = candidate.Isrc, Name = candidate.Name, NormalizedName = NameNormalizer.Normalize(candidate.Name), DurationMs = candidate.DurationMs,
            CreatedAtUtc = now, UpdatedAtUtc = now, EnrichmentStatus = candidate.Features is null ? EnrichmentStatus.Pending : EnrichmentStatus.Completed,
        };
        var artistOrder = 0;
        foreach (var artistName in candidate.Artists.DefaultIfEmpty("Unknown Artist"))
        {
            var normalized = NameNormalizer.Normalize(artistName);
            var artist = dbContext.Artists.Local.FirstOrDefault(x => x.NormalizedName == normalized)
                ?? await dbContext.Artists.FirstOrDefaultAsync(x => x.NormalizedName == normalized, cancellationToken)
                ?? new Artist { Name = artistName, NormalizedName = normalized, CreatedAtUtc = now, UpdatedAtUtc = now };
            if (!track.TrackArtists.Any(credit => ReferenceEquals(credit.Artist, artist)))
                track.TrackArtists.Add(new TrackArtist { Track = track, Artist = artist, ArtistOrder = (short)artistOrder++ });
        }
        if (!string.IsNullOrWhiteSpace(candidate.Album))
        {
            var albumName = NameNormalizer.Normalize(candidate.Album);
            var primary = track.TrackArtists.OrderBy(a => a.ArtistOrder).First().Artist;
            track.Album = await dbContext.Albums.FirstOrDefaultAsync(a => a.NormalizedName == albumName && a.PrimaryArtistId == primary.Id, cancellationToken)
                ?? new Album { Name = candidate.Album, NormalizedName = albumName, PrimaryArtist = primary, ImageUrl = candidate.ImageUrl, CreatedAtUtc = now, UpdatedAtUtc = now };
        }
        dbContext.Tracks.Add(track);
        dbContext.TrackExternalIds.Add(new TrackExternalId { Track = track, Provider = "ReccoBeats", ExternalId = candidate.ProviderId, CreatedAtUtc = now });
        if (candidate.Features is not null) dbContext.TrackFeatures.Add(ReccoBeats.ReccoBeatsTrackFeatureProvider.MapFeatures(track, candidate.Features));
        return track;
    }

    private async Task<RecordingCatalog> LoadRecordingCatalogAsync(IQueryable<long> trackIds, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Tracks.AsNoTracking().Where(x => trackIds.Contains(x.Id)).Select(x => new
        {
            x.SpotifyTrackId,
            x.Isrc,
            x.Name,
            Album = x.Album != null ? x.Album.Name : null,
            x.DurationMs,
            Artists = x.TrackArtists.OrderBy(ta => ta.ArtistOrder).Select(ta => ta.Artist.Name).ToArray(),
        }).ToListAsync(cancellationToken);

        return new RecordingCatalog(rows.Select(x => new RecordingIdentityInput(x.Isrc, x.SpotifyTrackId, x.Name, x.Artists, x.Album, x.DurationMs)));
    }

    private static ExternalTrack[] DistinctRecordings(IEnumerable<ExternalTrack> candidates)
    {
        var distinct = new List<ExternalTrack>();
        foreach (var candidate in candidates)
        {
            if (!distinct.Any(existing => Equivalent(existing, candidate))) distinct.Add(candidate);
        }
        return distinct.ToArray();
    }

    private static bool Equivalent(ExternalTrack left, ExternalTrack right) =>
        RecordingIdentityResolver.AreEquivalent(
            new RecordingIdentityInput(left.Isrc, left.SpotifyTrackId, left.Name, left.Artists, left.Album, left.DurationMs),
            new RecordingIdentityInput(right.Isrc, right.SpotifyTrackId, right.Name, right.Artists, right.Album, right.DurationMs));

    private sealed class RecordingCatalog
    {
        private readonly List<RecordingIdentityInput> identities = [];

        public RecordingCatalog(IEnumerable<RecordingIdentityInput> inputs)
        {
            foreach (var input in inputs)
            {
                identities.Add(input);
            }
        }

        public bool Contains(ExternalTrack track)
        {
            var candidate = new RecordingIdentityInput(track.Isrc, track.SpotifyTrackId, track.Name, track.Artists, track.Album, track.DurationMs);
            return identities.Any(identity => RecordingIdentityResolver.AreEquivalent(identity, candidate));
        }
    }

    private sealed record ScoredCandidate(ExternalTrack External, ScoreResult Score);
}
