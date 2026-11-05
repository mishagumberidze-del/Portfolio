using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Infrastructure.Preferences;

public sealed class PreferenceModelService : IPreferenceModelService
{
    private readonly SpotifyTasteDbContext dbContext;
    private readonly PreferenceModelOptions options;

    public PreferenceModelService(SpotifyTasteDbContext dbContext, IOptions<PreferenceModelOptions>? configuredOptions = null)
    {
        this.dbContext = dbContext;
        options = configuredOptions?.Value ?? new PreferenceModelOptions();
    }

    public async Task<PreferenceValidationDto> RebuildAsync(CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.SingleAsync(cancellationToken);
        var saved = user.SpotifyLibraryDataAvailable
            ? (await dbContext.UserSavedTracks.Where(x => x.UserId == user.Id).Select(x => x.TrackId).ToListAsync(cancellationToken)).ToHashSet()
            : [];
        // Saved truth belongs to the exported/API release, while preference evidence
        // may apply to the same recording on another album. Never fabricate plays
        // or extra UserSavedTrack rows to carry that derived signal.
        var catalog = await dbContext.Tracks.AsNoTracking().Select(t => new
        {
            t.Id, t.Isrc, t.SpotifyTrackId, t.Name, t.DurationMs,
            Album = t.Album != null ? t.Album.Name : null,
            Artists = t.TrackArtists.OrderBy(a => a.ArtistOrder).Select(a => a.Artist.Name).ToArray()
        }).ToListAsync(cancellationToken);
        var clusters = RecordingIdentityResolver.Cluster(catalog.Select(t => new RecordingIdentityInput(t.Isrc, t.SpotifyTrackId, t.Name, t.Artists, t.Album, t.DurationMs)).ToArray());
        var clusterByTrack = catalog.Select((t, i) => (t.Id, Cluster: clusters[i])).ToDictionary(t => t.Id, t => t.Cluster);
        var savedClusters = saved.Select(id => clusterByTrack[id]).ToHashSet();
        var savedEquivalent = catalog.Where(t => savedClusters.Contains(clusterByTrack[t.Id])).Select(t => t.Id).ToHashSet();
        var events = await dbContext.ListeningEvents.AsNoTracking().Where(x => x.UserId == user.Id)
            .Select(x => new { x.TrackId, x.PlayedAtUtc, x.MsPlayed, x.WasSkipped, x.Track.DurationMs }).ToListAsync(cancellationToken);

        var now = events.Count == 0 ? DateTime.UtcNow : events.Max(x => x.PlayedAtUtc).AddDays(1);
        var calculated = events.GroupBy(x => x.TrackId).Select(group =>
        {
            var observations = group.Select(x => new PreferenceObservation(x.PlayedAtUtc, x.MsPlayed, x.DurationMs, x.WasSkipped, savedEquivalent.Contains(x.TrackId))).ToList();
            return (TrackId: group.Key, Value: PreferenceModel.Calculate(observations, now, options), Plays: observations.Count, SkipRate: observations.Count(x => x.WasSkipped == true || x.MsPlayed < 10_000) / (double)observations.Count, Saved: savedEquivalent.Contains(group.Key));
        }).ToList();

        var existing = await dbContext.TrackPreferences.Where(x => x.UserId == user.Id && x.ModelVersion == options.ModelVersion).ToDictionaryAsync(x => x.TrackId, cancellationToken);
        foreach (var item in calculated)
        {
            if (!existing.TryGetValue(item.TrackId, out var row))
            {
                row = new TrackPreference { UserId = user.Id, TrackId = item.TrackId, ModelVersion = options.ModelVersion };
                dbContext.TrackPreferences.Add(row);
            }
            row.PositiveWeight = (decimal)item.Value.PositiveWeight;
            row.NegativeWeight = (decimal)item.Value.NegativeWeight;
            row.RecencyWeight = (decimal)item.Value.RecencyWeight;
            row.RepeatWeight = (decimal)item.Value.RepeatWeight;
            row.CompletionWeight = (decimal)item.Value.CompletionWeight;
            row.SkipPenalty = (decimal)item.Value.SkipPenalty;
            row.SavedBoost = (decimal)item.Value.SavedBoost;
            row.BehavioralScore = (decimal)item.Value.Score;
            row.CalculatedAtUtc = DateTime.UtcNow;
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        // Leave each feature-bearing track entirely out of its own neighborhood. The
        // held-out feature vector is scored against the remaining strong and weak
        // behavioral examples, so this exercises unseen-track audio matching.
        var byTrack = calculated.ToDictionary(x => x.TrackId);
        var featureRows = await dbContext.TrackFeatures.AsNoTracking().Where(x => x.Provider == "ReccoBeats").Select(feature => new
        {
            feature.TrackId,
            Vector = new TrackFeatureVector((double?)feature.Tempo, (double?)feature.Energy, (double?)feature.Danceability, (double?)feature.Valence, (double?)feature.Acousticness, (double?)feature.Instrumentalness, (double?)feature.Speechiness, (double?)feature.Liveness, (double?)feature.Loudness, feature.Key, feature.Mode, feature.TimeSignature),
        }).ToListAsync(cancellationToken);
        var eligible = featureRows.Where(x => byTrack.ContainsKey(x.TrackId)).Select(x => new
        {
            x.TrackId, x.Vector,
            Signal = byTrack[x.TrackId].Value.PositiveWeight - byTrack[x.TrackId].Value.NegativeWeight + byTrack[x.TrackId].Value.SavedBoost,
        }).OrderBy(x => x.Signal).ToArray();
        var quartile = eligible.Length / 4;
        // Keep equal behavioral evidence in the same group; catalog order must
        // never manufacture a strong/weak distinction at a quartile boundary.
        var separable = quartile > 0 && eligible[quartile - 1].Signal < eligible[^quartile].Signal;
        var weakIds = eligible.Where(x => separable && x.Signal <= eligible[quartile - 1].Signal).Select(x => x.TrackId).ToHashSet();
        var strongIds = eligible.Where(x => separable && x.Signal >= eligible[^quartile].Signal).Select(x => x.TrackId).ToHashSet();
        var holdouts = eligible.Select(candidate =>
        {
            var strongNeighbors = eligible.Where(x => clusterByTrack[x.TrackId] != clusterByTrack[candidate.TrackId] && strongIds.Contains(x.TrackId)).Select(x => (x.Vector, Math.Max(.01, x.Signal)));
            var weakNeighbors = eligible.Where(x => clusterByTrack[x.TrackId] != clusterByTrack[candidate.TrackId] && weakIds.Contains(x.TrackId)).Select(x => (x.Vector, 1d));
            var strongSimilarity = PreferenceModel.WeightedSimilarity(candidate.Vector, strongNeighbors);
            var weakSimilarity = PreferenceModel.WeightedSimilarity(candidate.Vector, weakNeighbors);
            double? score = strongSimilarity.HasValue && weakSimilarity.HasValue
                ? Math.Clamp(10 * (.72 * strongSimilarity.Value + .28 * (1 - weakSimilarity.Value)), 0, 10) : null;
            return (candidate.TrackId, Score: score);
        }).Where(x => x.Score.HasValue).ToArray();
        var positives = holdouts.Where(x => strongIds.Contains(x.TrackId)).Select(x => x.Score!.Value).ToArray();
        var negatives = holdouts.Where(x => weakIds.Contains(x.TrackId)).Select(x => x.Score!.Value).ToArray();
        return new PreferenceValidationDto(calculated.Count, positives.Length, negatives.Length, positives.Length == 0 ? null : Math.Round(positives.Average(), 3), negatives.Length == 0 ? null : Math.Round(negatives.Average(), 3), PairwiseConcordance(positives, negatives), options.ModelVersion, "Leave-one-recording-out feature-neighborhood validation (all equivalent releases excluded; tied behavioral evidence stays together; both neighborhoods required; score differences below 0.0001 receive half credit)");
    }

    public static double? PairwiseConcordance(IReadOnlyList<double> positives, IReadOnlyList<double> negatives)
    {
        if (positives.Count == 0 || negatives.Count == 0) return null;
        double credit = 0;
        foreach (var positive in positives)
            foreach (var negative in negatives)
                credit += Math.Abs(positive - negative) < 0.0001 ? 0.5 : positive > negative ? 1 : 0;
        return Math.Round(credit / ((long)positives.Count * negatives.Count), 4);
    }
}
