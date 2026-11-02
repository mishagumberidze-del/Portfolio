using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.ReccoBeats;

namespace SpotifyTaste.Infrastructure.Preferences;

public sealed class SongScoringService : ISongScoringService
{
    private const string Provider = "ReccoBeats";
    private const int NeighborhoodSize = 40;
    private readonly SpotifyTasteDbContext dbContext;
    private readonly IReccoBeatsClient reccoBeatsClient;
    private readonly IPreferenceModelService preferenceModelService;
    private readonly PreferenceModelOptions options;
    private List<PreferencePoint>? cachedPreferences;
    private List<RecentPoint>? cachedRecent;
    private Dictionary<string, long>? cachedArtistTotals;
    private Dictionary<string, HistoricalPoint>? cachedHistoricalBySpotifyId;
    private List<HistoricalRecording>? cachedHistoricalRecordings;
    private HashSet<long>? cachedSavedTrackIds;
    private List<FeaturePoint>? cachedSavedVectors;
    private bool cachedLibraryAvailable;
    private Dictionary<string, ExternalTrack>? primedTrackCache;
    private readonly HashSet<string> verifiedTrackCache = new(StringComparer.OrdinalIgnoreCase);

    public SongScoringService(SpotifyTasteDbContext dbContext, IReccoBeatsClient reccoBeatsClient, IPreferenceModelService preferenceModelService, IOptions<PreferenceModelOptions>? configuredOptions = null)
    {
        this.dbContext = dbContext;
        this.reccoBeatsClient = reccoBeatsClient;
        this.preferenceModelService = preferenceModelService;
        options = configuredOptions?.Value ?? new PreferenceModelOptions();
    }

    public async Task<ScoreResult> ScoreAsync(string spotifyUrlOrUri, CancellationToken cancellationToken = default)
    {
        var spotifyId = SpotifyTrackIdParser.Parse(spotifyUrlOrUri);
        var external = await GetCachedAsync(spotifyId, cancellationToken)
            ?? await reccoBeatsClient.GetBySpotifyIdAsync(spotifyId, cancellationToken)
            ?? throw new KeyNotFoundException("ReccoBeats could not find this Spotify track ID.");
        return await ScoreExternalAsync(external, cancellationToken);
    }

    public async Task<ScoreResult> ScoreExternalAsync(ExternalTrack external, CancellationToken cancellationToken = default)
    {
        if (external.SpotifyTrackId is null) throw new InvalidDataException("Candidate has no Spotify track identity.");
        if (!verifiedTrackCache.Contains(external.SpotifyTrackId))
        {
            await CacheAsync(external, cancellationToken);
            verifiedTrackCache.Add(external.SpotifyTrackId);
        }
        if (!await dbContext.TrackPreferences.AnyAsync(x => x.ModelVersion == options.ModelVersion, cancellationToken))
            await preferenceModelService.RebuildAsync(cancellationToken);
        await EnsureScoringContextAsync(cancellationToken);

        var preferenceRows = cachedPreferences!;
        var historicalTrack = ResolveHistory(external);
        var excludedTrackIds = historicalTrack?.TrackIds ?? [];

        var candidate = external.Features;
        var positiveSimilarity = candidate is null ? null : TopKSimilarity(candidate,
            preferenceRows.Where(x => x.PositiveWeight > x.NegativeWeight)
                .Select(x => new NeighborPoint(x.TrackId, x.Vector, x.PositiveWeight - x.NegativeWeight)), excludedTrackIds);
        var negativeSimilarity = candidate is null ? null : TopKSimilarity(candidate,
            preferenceRows.Where(x => x.NegativeWeight > x.PositiveWeight)
                .Select(x => new NeighborPoint(x.TrackId, x.Vector, x.NegativeWeight - x.PositiveWeight)), excludedTrackIds);
        var lifetimeTasteValue = ContrastTaste(positiveSimilarity, negativeSimilarity);
        var lifetimeTaste = lifetimeTasteValue ?? 0.5;

        var recentWeighted = cachedRecent!;
        var recentPositiveSimilarity = candidate is null ? null : TopKSimilarity(candidate,
            recentWeighted.Where(x => x.PositiveWeight > x.NegativeWeight)
                .Select(x => new NeighborPoint(x.TrackId, x.Vector, x.PositiveWeight - x.NegativeWeight)), excludedTrackIds);
        var recentNegativeSimilarity = candidate is null ? null : TopKSimilarity(candidate,
            recentWeighted.Where(x => x.NegativeWeight > x.PositiveWeight)
                .Select(x => new NeighborPoint(x.TrackId, x.Vector, x.NegativeWeight - x.PositiveWeight)), excludedTrackIds);
        var recentTasteValue = ContrastTaste(recentPositiveSimilarity, recentNegativeSimilarity);
        var recentTaste = recentTasteValue ?? 0.5;
        var featureMatchValue = WeightedAvailable((positiveSimilarity?.Value, 0.60), (lifetimeTasteValue, 0.40));
        var featureMatch = featureMatchValue ?? 0.5;

        var normalizedArtists = external.Artists.Select(NameNormalizer.Normalize).ToArray();
        var artistTotals = cachedArtistTotals!;
        var topArtistMs = artistTotals.Count == 0 ? 1 : artistTotals.Values.Max();
        var candidateArtistMs = normalizedArtists.Where(artistTotals.ContainsKey).Select(x => artistTotals[x]).DefaultIfEmpty(0).Max();
        double? artistAffinityValue = normalizedArtists.Length == 0
            ? null
            : candidateArtistMs == 0
                ? 0.45
                : Math.Clamp(Math.Log10(1 + candidateArtistMs) / Math.Log10(1 + topArtistMs), 0, 1);
        var artistAffinity = artistAffinityValue ?? 0.5;

        var knownRecording = historicalTrack is { EventCount: > 0 };
        var historicalBehavior = !knownRecording || historicalTrack?.Preference is null ? 0.5 : Math.Clamp(historicalTrack.Preference.Value / 10, 0, 1);

        var libraryAvailable = cachedLibraryAvailable;
        var savedTrackSimilarity = 0.5;
        var savedSimilarityAvailable = false;
        if (libraryAvailable)
        {
            if (historicalTrack?.IsSaved == true)
            {
                savedTrackSimilarity = 1;
                savedSimilarityAvailable = true;
            }
            else if (candidate is not null && cachedSavedVectors!.Count > 0)
            {
                var savedSimilarity = TopKSimilarity(candidate, cachedSavedVectors.Select(x => new NeighborPoint(x.TrackId, x.Vector, 1)), excludedTrackIds);
                if (savedSimilarity is not null)
                {
                    savedTrackSimilarity = savedSimilarity.Value;
                    savedSimilarityAvailable = true;
                }
            }
        }

        // History is deliberately absent from the generalized prediction. Signals
        // that cannot be observed are omitted and the remaining weights are
        // normalized, rather than silently treating missing evidence as a 5/10.
        var generalized = WeightedAvailable(
            (featureMatchValue, 0.30),
            (recentTasteValue, 0.22),
            (lifetimeTasteValue, 0.18),
            (candidateArtistMs > 0 ? artistAffinityValue : null, 0.12),
            (savedSimilarityAvailable ? savedTrackSimilarity : null, 0.08)) ?? 0.5;

        var exploration = !knownRecording && featureMatchValue.HasValue
            ? featureMatch switch { >= 0.72 => 0, >= 0.48 => 0.18, >= 0.30 => -0.25, _ => -0.65 }
            : 0;
        var generalizedScore = Math.Clamp(10 * generalized + exploration, 0, 10);

        // PositiveWeight + NegativeWeight is time-decayed evidence mass. A single
        // listen has little influence, while sustained (positive or negative)
        // behavior increasingly controls the known-recording score. Equivalent
        // Spotify releases contribute to the same mass through their ISRC.
        var observedBlendWeight = !knownRecording || historicalTrack?.Preference is null
            ? 0
            : 0.85 * historicalTrack.EvidenceMass / (historicalTrack.EvidenceMass + 5);
        var raw = observedBlendWeight * (historicalTrack?.Preference ?? 5) + (1 - observedBlendWeight) * generalizedScore;
        var final = Math.Round(Math.Clamp(raw, 0, 10), 1);
        var coverage = (candidate is null ? 0 : 1) + (positiveSimilarity?.Count >= 20 ? 1 : 0) + (recentPositiveSimilarity?.Count >= 20 ? 1 : 0) + (candidateArtistMs > 0 ? 1 : 0) + (observedBlendWeight >= 0.5 ? 1 : 0);
        var confidence = coverage >= 4 ? "High" : coverage >= 2 ? "Medium" : "Low";
        var components = new ScoreComponents(Round10(featureMatch), Round10(artistAffinity), Round10(recentTaste), Round10(lifetimeTaste), Round10(historicalBehavior), Round10(savedTrackSimilarity), Math.Round(exploration, 2));
        return new ScoreResult(external.SpotifyTrackId, external.Name, external.Artists, final, confidence, components,
            Explain(components, candidate is not null, libraryAvailable, knownRecording, historicalTrack?.EventCount ?? 0, observedBlendWeight), knownRecording, historicalTrack?.EventCount ?? 0);
    }

    public async Task PrimeTrackCacheAsync(IEnumerable<string> spotifyTrackIds, CancellationToken cancellationToken = default)
    {
        var ids = spotifyTrackIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).Take(1000).ToArray();
        var tracks = await dbContext.Tracks.AsNoTracking().Include(x => x.Album).Include(x => x.TrackArtists).ThenInclude(x => x.Artist)
            .Where(x => x.SpotifyTrackId != null && ids.Contains(x.SpotifyTrackId)).ToListAsync(cancellationToken);
        var trackIds = tracks.Select(x => x.Id).ToArray();
        var features = await dbContext.TrackFeatures.AsNoTracking().Where(x => trackIds.Contains(x.TrackId) && x.Provider == Provider).ToDictionaryAsync(x => x.TrackId, cancellationToken);
        var externalIds = await dbContext.TrackExternalIds.AsNoTracking().Where(x => trackIds.Contains(x.TrackId) && x.Provider == Provider).ToDictionaryAsync(x => x.TrackId, x => x.ExternalId, cancellationToken);
        primedTrackCache = new Dictionary<string, ExternalTrack>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in tracks)
        {
            if (track.SpotifyTrackId is null || !features.TryGetValue(track.Id, out var feature) || !externalIds.TryGetValue(track.Id, out var providerId)) continue;
            primedTrackCache[track.SpotifyTrackId] = ToExternal(track, feature, providerId);
        }
    }

    private async Task EnsureScoringContextAsync(CancellationToken cancellationToken)
    {
        if (cachedPreferences is not null) return;

        // Load the three large history inputs independently. In particular, avoid
        // a per-track correlated preference/count subquery when constructing the
        // exact-ID and ISRC recording maps.
        var preferenceRows = await dbContext.TrackPreferences.AsNoTracking()
            .Where(x => x.ModelVersion == options.ModelVersion)
            .ToListAsync(cancellationToken);
        var featureRows = await dbContext.TrackFeatures.AsNoTracking()
            .Where(x => x.Provider == Provider)
            .ToListAsync(cancellationToken);
        var featureByTrack = featureRows.ToDictionary(x => x.TrackId, ToVector);
        cachedPreferences = preferenceRows
            .Where(x => featureByTrack.ContainsKey(x.TrackId))
            .Select(x => new PreferencePoint(x.TrackId, (double)(x.PositiveWeight + x.SavedBoost), (double)x.NegativeWeight, (double)x.BehavioralScore, featureByTrack[x.TrackId]))
            .ToList();

        var latestListen = await dbContext.ListeningEvents.MaxAsync(x => (DateTime?)x.PlayedAtUtc, cancellationToken) ?? DateTime.UtcNow;
        var recentCutoff = latestListen.AddDays(-90);
        var recentRows = await dbContext.ListeningEvents.AsNoTracking()
            .Where(x => x.PlayedAtUtc >= recentCutoff)
            .Select(listeningEvent => new
            {
                listeningEvent.TrackId, listeningEvent.PlayedAtUtc, listeningEvent.MsPlayed, listeningEvent.WasSkipped, listeningEvent.Track.DurationMs,
            }).ToListAsync(cancellationToken);
        cachedRecent = recentRows.Where(x => featureByTrack.ContainsKey(x.TrackId)).GroupBy(x => x.TrackId).Select(group =>
        {
            var behavior = PreferenceModel.Calculate(group.Select(x => new PreferenceObservation(x.PlayedAtUtc, x.MsPlayed, x.DurationMs, x.WasSkipped, false)).ToList(), latestListen.AddSeconds(1), options);
            return new RecentPoint(group.Key, featureByTrack[group.Key], behavior.PositiveWeight, behavior.NegativeWeight);
        }).ToList();

        cachedArtistTotals = (await dbContext.ListeningEvents.AsNoTracking()
            .SelectMany(x => x.Track.TrackArtists.Select(ta => new { ta.Artist.NormalizedName, x.MsPlayed }))
            .GroupBy(x => x.NormalizedName).Select(group => new { Name = group.Key, Ms = group.Sum(x => (long)x.MsPlayed) })
            .ToListAsync(cancellationToken)).ToDictionary(x => x.Name, x => x.Ms, StringComparer.Ordinal);

        cachedLibraryAvailable = await dbContext.Users.Select(x => x.SpotifyLibraryDataAvailable).SingleOrDefaultAsync(cancellationToken);
        cachedSavedTrackIds = (await dbContext.UserSavedTracks.AsNoTracking().Select(x => x.TrackId).ToListAsync(cancellationToken)).ToHashSet();
        cachedSavedVectors = [];

        var trackRows = await dbContext.Tracks.AsNoTracking()
            .Select(x => new
            {
                x.Id,
                x.SpotifyTrackId,
                x.Isrc,
                x.Name,
                x.DurationMs,
                Album = x.Album != null ? x.Album.Name : null,
                Artists = x.TrackArtists.OrderBy(ta => ta.ArtistOrder).Select(ta => ta.Artist.Name).ToArray(),
            })
            .ToListAsync(cancellationToken);
        var eventCountRows = await dbContext.ListeningEvents.AsNoTracking()
            .GroupBy(x => x.TrackId)
            .Select(group => new { TrackId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);
        var eventCounts = eventCountRows.ToDictionary(x => x.TrackId, x => x.Count);
        var preferencesByTrack = preferenceRows.ToDictionary(x => x.TrackId);

        cachedHistoricalBySpotifyId = new Dictionary<string, HistoricalPoint>(StringComparer.OrdinalIgnoreCase);
        cachedHistoricalRecordings = [];
        var identityInputs = trackRows.Select(x => new RecordingIdentityInput(x.Isrc, x.SpotifyTrackId, x.Name, x.Artists, x.Album, x.DurationMs)).ToArray();
        var clusterIds = RecordingIdentityResolver.Cluster(identityInputs);
        var recordingGroups = trackRows.Select((row, index) => new { Row = row, Identity = identityInputs[index], Cluster = clusterIds[index] }).GroupBy(x => x.Cluster);
        foreach (var recording in recordingGroups)
        {
            var rows = recording.Select(x => x.Row).ToArray();
            var trackIds = rows.Select(x => x.Id).ToHashSet();
            var recordingPreferences = trackIds.Where(preferencesByTrack.ContainsKey).Select(id => preferencesByTrack[id]).ToArray();
            var evidenceMass = recordingPreferences.Sum(x => (double)(x.PositiveWeight + x.NegativeWeight));
            double? preference = null;
            if (recordingPreferences.Length > 0)
            {
                preference = evidenceMass > 0
                    ? recordingPreferences.Sum(x => (double)x.BehavioralScore * (double)(x.PositiveWeight + x.NegativeWeight)) / evidenceMass
                    : recordingPreferences.Average(x => (double)x.BehavioralScore);
            }

            var point = new HistoricalPoint(
                trackIds,
                preference,
                trackIds.Sum(id => eventCounts.GetValueOrDefault(id)),
                evidenceMass,
                trackIds.Any(cachedSavedTrackIds.Contains));
            // One vector per saved recording, with no arbitrary first-500 cutoff.
            // A cached equivalent release may supply features without pretending
            // that the provider returned them for the saved release itself.
            if (point.IsSaved)
            {
                var featured = trackIds.Where(featureByTrack.ContainsKey)
                    .OrderByDescending(cachedSavedTrackIds.Contains).ThenBy(id => id).Cast<long?>().FirstOrDefault();
                if (featured is { } id) cachedSavedVectors.Add(new FeaturePoint(id, featureByTrack[id]));
            }
            foreach (var row in rows.Where(x => !string.IsNullOrWhiteSpace(x.SpotifyTrackId)))
                cachedHistoricalBySpotifyId[row.SpotifyTrackId!] = point;
            cachedHistoricalRecordings.Add(new HistoricalRecording(recording.Select(x => x.Identity).ToArray(), point));
        }
    }

    private async Task<ExternalTrack?> GetCachedAsync(string spotifyTrackId, CancellationToken cancellationToken)
    {
        if (primedTrackCache is not null)
        {
            if (primedTrackCache.TryGetValue(spotifyTrackId, out var primed)) verifiedTrackCache.Add(spotifyTrackId);
            return primed;
        }
        var track = await dbContext.Tracks.AsNoTracking().Include(x => x.Album).Include(x => x.TrackArtists).ThenInclude(x => x.Artist)
            .SingleOrDefaultAsync(x => x.SpotifyTrackId == spotifyTrackId, cancellationToken);
        if (track is null) return null;
        var feature = await dbContext.TrackFeatures.AsNoTracking().SingleOrDefaultAsync(x => x.TrackId == track.Id && x.Provider == Provider, cancellationToken);
        if (feature is null) return null;
        var providerId = await dbContext.TrackExternalIds.AsNoTracking().Where(x => x.TrackId == track.Id && x.Provider == Provider).Select(x => x.ExternalId).SingleOrDefaultAsync(cancellationToken);
        if (providerId is null) return null;
        verifiedTrackCache.Add(spotifyTrackId);
        return ToExternal(track, feature, providerId);
    }

    private static ExternalTrack ToExternal(Track track, TrackFeature feature, string providerId) => new(
        providerId, track.SpotifyTrackId, track.Name, track.TrackArtists.OrderBy(x => x.ArtistOrder).Select(x => x.Artist.Name).ToList(), track.Album?.Name,
        track.DurationMs, track.Isrc, track.SpotifyUrl, track.Album?.ImageUrl,
        new TrackFeatureVector((double?)feature.Tempo, (double?)feature.Energy, (double?)feature.Danceability, (double?)feature.Valence, (double?)feature.Acousticness, (double?)feature.Instrumentalness, (double?)feature.Speechiness, (double?)feature.Liveness, (double?)feature.Loudness, feature.Key, feature.Mode, feature.TimeSignature));

    private async Task CacheAsync(ExternalTrack external, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var track = await dbContext.Tracks.Include(x => x.TrackArtists).ThenInclude(x => x.Artist).Include(x => x.Album)
            .SingleOrDefaultAsync(x => x.SpotifyTrackId == external.SpotifyTrackId, cancellationToken);
        if (track is null)
        {
            var artists = new List<Artist>();
            foreach (var artistName in external.Artists.DefaultIfEmpty("Unknown Artist"))
            {
                var normalized = NameNormalizer.Normalize(artistName);
                var artist = dbContext.Artists.Local.FirstOrDefault(x => x.NormalizedName == normalized)
                    ?? await dbContext.Artists.FirstOrDefaultAsync(x => x.NormalizedName == normalized, cancellationToken)
                    ?? new Artist { Name = artistName, NormalizedName = normalized, CreatedAtUtc = now, UpdatedAtUtc = now };
                if (!artists.Contains(artist)) artists.Add(artist);
            }
            Album? album = null;
            if (!string.IsNullOrWhiteSpace(external.Album))
            {
                var normalizedAlbum = NameNormalizer.Normalize(external.Album);
                var primaryArtist = artists[0];
                album = dbContext.Albums.Local.FirstOrDefault(x => x.NormalizedName == normalizedAlbum && x.PrimaryArtist == primaryArtist)
                    ?? await dbContext.Albums.FirstOrDefaultAsync(x => x.NormalizedName == normalizedAlbum && x.PrimaryArtist != null && x.PrimaryArtist.NormalizedName == primaryArtist.NormalizedName, cancellationToken)
                    ?? new Album { Name = external.Album, NormalizedName = normalizedAlbum, PrimaryArtist = primaryArtist, ImageUrl = external.ImageUrl, CreatedAtUtc = now, UpdatedAtUtc = now };
            }
            track = new Track
            {
                SpotifyTrackId = external.SpotifyTrackId, SpotifyUri = $"spotify:track:{external.SpotifyTrackId}", SpotifyUrl = external.SpotifyUrl,
                Isrc = external.Isrc, Name = external.Name, NormalizedName = NameNormalizer.Normalize(external.Name), Album = album,
                DurationMs = external.DurationMs, CreatedAtUtc = now, UpdatedAtUtc = now,
            };
            for (short index = 0; index < artists.Count; index++) track.TrackArtists.Add(new TrackArtist { Track = track, Artist = artists[index], ArtistOrder = index });
            dbContext.Tracks.Add(track);
        }
        else
        {
            track.DurationMs ??= external.DurationMs;
            track.Isrc ??= external.Isrc;
            track.SpotifyUrl ??= external.SpotifyUrl;
            if (track.Album is not null) track.Album.ImageUrl ??= external.ImageUrl;
            track.UpdatedAtUtc = now;
        }

        var externalId = dbContext.TrackExternalIds.Local.FirstOrDefault(x => x.Track == track && x.Provider == Provider)
            ?? (track.Id == 0 ? null : await dbContext.TrackExternalIds.SingleOrDefaultAsync(x => x.TrackId == track.Id && x.Provider == Provider, cancellationToken));
        if (externalId is null) dbContext.TrackExternalIds.Add(new TrackExternalId { Track = track, Provider = Provider, ExternalId = external.ProviderId, CreatedAtUtc = now });

        if (external.Features is not null)
        {
            var feature = dbContext.TrackFeatures.Local.FirstOrDefault(x => x.Track == track && x.Provider == Provider)
                ?? (track.Id == 0 ? null : await dbContext.TrackFeatures.SingleOrDefaultAsync(x => x.TrackId == track.Id && x.Provider == Provider, cancellationToken));
            if (feature is null) dbContext.TrackFeatures.Add(ReccoBeatsTrackFeatureProvider.MapFeatures(track, external.Features));
            else ApplyFeatureValues(feature, external.Features, now);
            track.EnrichmentStatus = EnrichmentStatus.Completed;
            track.EnrichmentFailureReason = null;
            track.EnrichmentLastAttemptAtUtc = now;
        }
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static void ApplyFeatureValues(TrackFeature feature, TrackFeatureVector value, DateTime fetchedAtUtc)
    {
        feature.Tempo = Decimal(value.Tempo); feature.Energy = Decimal(value.Energy); feature.Danceability = Decimal(value.Danceability); feature.Valence = Decimal(value.Valence);
        feature.Acousticness = Decimal(value.Acousticness); feature.Instrumentalness = Decimal(value.Instrumentalness); feature.Speechiness = Decimal(value.Speechiness); feature.Liveness = Decimal(value.Liveness); feature.Loudness = Decimal(value.Loudness);
        feature.Key = value.Key; feature.Mode = value.Mode; feature.TimeSignature = value.TimeSignature; feature.FetchedAtUtc = fetchedAtUtc;
    }

    private HistoricalPoint? ResolveHistory(ExternalTrack external)
    {
        if (cachedHistoricalBySpotifyId!.TryGetValue(external.SpotifyTrackId!, out var bySpotifyId)) return bySpotifyId;
        var input = new RecordingIdentityInput(external.Isrc, external.SpotifyTrackId, external.Name, external.Artists, external.Album, external.DurationMs);
        return cachedHistoricalRecordings!.FirstOrDefault(recording => recording.Identities.Any(identity => RecordingIdentityResolver.AreEquivalent(identity, input)))?.Point;
    }

    private static SimilarityResult? TopKSimilarity(TrackFeatureVector candidate, IEnumerable<NeighborPoint> examples, IReadOnlySet<long> excludedTrackIds)
    {
        var nearest = new PriorityQueue<(double Similarity, double Weight), double>();
        foreach (var example in examples)
        {
            if (excludedTrackIds.Contains(example.TrackId) || example.Weight <= 0) continue;
            var similarity = PreferenceModel.FeatureSimilarity(candidate, example.Vector);
            if (!double.IsFinite(similarity)) continue;
            var boundedWeight = Math.Clamp(Math.Log2(1 + example.Weight), 0.25, 8);
            nearest.Enqueue((similarity, boundedWeight), similarity);
            if (nearest.Count > NeighborhoodSize) nearest.Dequeue();
        }
        if (nearest.Count == 0) return null;
        var neighbors = nearest.UnorderedItems.Select(x => x.Element).ToArray();
        var totalWeight = neighbors.Sum(x => x.Weight);
        return totalWeight <= 0 ? null : new SimilarityResult(neighbors.Sum(x => x.Similarity * x.Weight) / totalWeight, neighbors.Length);
    }

    private static double? ContrastTaste(SimilarityResult? positive, SimilarityResult? negative) =>
        WeightedAvailable((positive?.Value, 0.72), (negative is null ? null : 1 - negative.Value, 0.28));

    private static double? WeightedAvailable(params (double? Value, double Weight)[] signals)
    {
        var available = signals.Where(x => x.Value.HasValue && x.Weight > 0).ToArray();
        var totalWeight = available.Sum(x => x.Weight);
        return totalWeight <= 0 ? null : Math.Clamp(available.Sum(x => x.Value!.Value * x.Weight) / totalWeight, 0, 1);
    }

    private static TrackFeatureVector ToVector(TrackFeature feature) => new(
        (double?)feature.Tempo, (double?)feature.Energy, (double?)feature.Danceability, (double?)feature.Valence,
        (double?)feature.Acousticness, (double?)feature.Instrumentalness, (double?)feature.Speechiness,
        (double?)feature.Liveness, (double?)feature.Loudness, feature.Key, feature.Mode, feature.TimeSignature);

    private static decimal? Decimal(double? value) => value.HasValue ? (decimal)value.Value : null;

    private static IReadOnlyList<string> Explain(ScoreComponents components, bool hasFeatures, bool libraryAvailable, bool knownRecording, int eventCount, double observedBlendWeight)
    {
        var explanations = new List<string>();
        if (knownRecording && observedBlendWeight >= 0.5)
            explanations.Add($"Strong observed listening evidence across {eventCount:N0} plays is the dominant signal for this recording and its equivalent Spotify releases.");
        else if (knownRecording)
            explanations.Add($"Observed listening evidence from {eventCount:N0} play{(eventCount == 1 ? "" : "s")} is included cautiously until the track has a stronger repeat history.");
        if (!libraryAvailable) explanations.Add("Saved/liked library data was not present, so that signal is excluded from the score rather than inferred.");
        if (!hasFeatures) explanations.Add("ReccoBeats metadata was found, but audio features were unavailable, so the score relies more on artist and history signals.");
        else if (components.FeatureMatch >= 7.5) explanations.Add("Strong match with the audio-feature profile of tracks you repeatedly listen to.");
        else if (components.FeatureMatch < 4.5) explanations.Add("Its audio characteristics sit outside your strongest historical preference neighborhoods.");
        if (components.RecentTasteMatch - components.LifetimeTasteMatch >= 1) explanations.Add("It matches your last-90-day direction more closely than your lifetime taste.");
        else if (components.LifetimeTasteMatch - components.RecentTasteMatch >= 1) explanations.Add("It fits your lifetime taste better than your recent listening direction.");
        if (components.ArtistTasteMatch >= 7.5) explanations.Add("The artist is strongly represented in your listening history.");
        else if (components.ArtistTasteMatch <= 4.6) explanations.Add("The artist is unfamiliar or lightly represented, increasing exploration uncertainty.");
        return explanations.Take(3).ToList();
    }

    private static double Round10(double value) => Math.Round(Math.Clamp(value, 0, 1) * 10, 1);

    private sealed record PreferencePoint(long TrackId, double PositiveWeight, double NegativeWeight, double BehavioralScore, TrackFeatureVector Vector);
    private sealed record RecentPoint(long TrackId, TrackFeatureVector Vector, double PositiveWeight, double NegativeWeight);
    private sealed record FeaturePoint(long TrackId, TrackFeatureVector Vector);
    private sealed record NeighborPoint(long TrackId, TrackFeatureVector Vector, double Weight);
    private sealed record SimilarityResult(double Value, int Count);
    private sealed record HistoricalPoint(HashSet<long> TrackIds, double? Preference, int EventCount, double EvidenceMass, bool IsSaved);
    private sealed record HistoricalRecording(IReadOnlyList<RecordingIdentityInput> Identities, HistoricalPoint Point);
}
