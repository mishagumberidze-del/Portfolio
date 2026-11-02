using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Infrastructure.ReccoBeats;

public sealed class ReccoBeatsTrackFeatureProvider(SpotifyTasteDbContext dbContext, IReccoBeatsClient client) : ITrackFeatureProvider
{
    private const string Provider = "ReccoBeats";

    public async Task<int> EnrichMissingAsync(int limit, CancellationToken cancellationToken = default) =>
        (await EnrichBatchAsync(limit, historicalTracksOnly: false, cancellationToken: cancellationToken)).Completed;

    public async Task<TrackEnrichmentBatchResult> EnrichBatchAsync(
        int limit,
        bool historicalTracksOnly = false,
        CancellationToken cancellationToken = default)
    {
        // Keep selection as small, predictable queries. SQL Server can choose a very
        // expensive plan when TOP, the aggregate and the wide Tracks row are combined.
        var eligibleTracks = dbContext.Tracks.AsNoTracking()
            .Where(track => track.SpotifyTrackId != null
                && (track.EnrichmentStatus == EnrichmentStatus.Pending || track.EnrichmentStatus == EnrichmentStatus.RetryLater));
        if (historicalTracksOnly)
            eligibleTracks = eligibleTracks.Where(track => dbContext.ListeningEvents.Any(listen => listen.TrackId == track.Id));

        var eligibleTrackIds = await eligibleTracks
            .Select(track => track.Id)
            .ToListAsync(cancellationToken);
        var playCounts = await dbContext.ListeningEvents.AsNoTracking()
            .GroupBy(listen => listen.TrackId)
            .Select(group => new { TrackId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.TrackId, x => x.Count, cancellationToken);
        var selectedTrackIds = eligibleTrackIds
            .OrderByDescending(trackId => playCounts.GetValueOrDefault(trackId))
            .ThenBy(trackId => trackId)
            .Take(Math.Clamp(limit, 1, 1000))
            .ToArray();
        var selectedTracks = await dbContext.Tracks
            .Where(track => selectedTrackIds.Contains(track.Id))
            .ToDictionaryAsync(track => track.Id, cancellationToken);
        var tracks = selectedTrackIds.Select(trackId => selectedTracks[trackId]).ToList();

        using var gate = new SemaphoreSlim(3);
        var results = await Task.WhenAll(tracks.Select(async track =>
        {
            await gate.WaitAsync(cancellationToken);
            try { return (Track: track, External: await client.GetBySpotifyIdAsync(track.SpotifyTrackId!, cancellationToken), Error: (Exception?)null); }
            catch (Exception exception) when (exception is not OperationCanceledException) { return (Track: track, External: (ExternalTrack?)null, Error: exception); }
            finally { gate.Release(); }
        }));

        var resolvedTrackIds = results
            .Where(x => x.Error is null && x.External is not null)
            .Select(x => x.Track.Id)
            .ToArray();
        var mappedTrackIds = (await dbContext.TrackExternalIds.AsNoTracking()
            .Where(x => x.Provider == Provider && resolvedTrackIds.Contains(x.TrackId))
            .Select(x => x.TrackId)
            .ToListAsync(cancellationToken))
            .ToHashSet();
        var featuredTrackIds = (await dbContext.TrackFeatures.AsNoTracking()
            .Where(x => x.Provider == Provider && resolvedTrackIds.Contains(x.TrackId))
            .Select(x => x.TrackId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var completed = 0;
        var notFound = 0;
        var retryLater = 0;
        foreach (var result in results)
        {
            result.Track.EnrichmentLastAttemptAtUtc = DateTime.UtcNow;
            if (result.Error is not null)
            {
                result.Track.EnrichmentStatus = EnrichmentStatus.RetryLater;
                result.Track.EnrichmentFailureReason = Truncate(result.Error.Message, 500);
                retryLater++;
                continue;
            }
            if (result.External is null)
            {
                result.Track.EnrichmentStatus = EnrichmentStatus.NotFound;
                result.Track.EnrichmentFailureReason = "Spotify track ID was not found by ReccoBeats.";
                notFound++;
                continue;
            }
            ApplyMetadata(result.Track, result.External);
            if (mappedTrackIds.Add(result.Track.Id))
                dbContext.TrackExternalIds.Add(new TrackExternalId { Track = result.Track, Provider = Provider, ExternalId = result.External.ProviderId, CreatedAtUtc = DateTime.UtcNow });
            if (result.External.Features is not null)
            {
                if (featuredTrackIds.Add(result.Track.Id))
                    dbContext.TrackFeatures.Add(MapFeatures(result.Track, result.External.Features));
                result.Track.EnrichmentStatus = EnrichmentStatus.Completed;
                result.Track.EnrichmentFailureReason = null;
                completed++;
            }
            else
            {
                result.Track.EnrichmentStatus = EnrichmentStatus.NotFound;
                result.Track.EnrichmentFailureReason = "ReccoBeats mapping was found, but audio features were unavailable.";
                notFound++;
            }
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        var remainingEligible = await CountEligibleAsync(historicalTracksOnly, cancellationToken);
        return new TrackEnrichmentBatchResult(tracks.Count, completed, notFound, retryLater, remainingEligible);
    }

    private Task<int> CountEligibleAsync(bool historicalTracksOnly, CancellationToken cancellationToken)
    {
        var query = dbContext.Tracks.AsNoTracking()
            .Where(track => track.SpotifyTrackId != null
                && (track.EnrichmentStatus == EnrichmentStatus.Pending || track.EnrichmentStatus == EnrichmentStatus.RetryLater));
        if (historicalTracksOnly)
            query = query.Where(track => dbContext.ListeningEvents.Any(listen => listen.TrackId == track.Id));
        return query.CountAsync(cancellationToken);
    }

    private static void ApplyMetadata(Track track, ExternalTrack external)
    {
        track.DurationMs ??= external.DurationMs;
        track.Isrc ??= external.Isrc;
        track.SpotifyUrl ??= external.SpotifyUrl;
        track.UpdatedAtUtc = DateTime.UtcNow;
    }

    internal static TrackFeature MapFeatures(Track track, TrackFeatureVector features) => new()
    {
        Track = track, Provider = Provider, FetchedAtUtc = DateTime.UtcNow,
        Tempo = ToDecimal(features.Tempo), Energy = ToDecimal(features.Energy), Danceability = ToDecimal(features.Danceability), Valence = ToDecimal(features.Valence),
        Acousticness = ToDecimal(features.Acousticness), Instrumentalness = ToDecimal(features.Instrumentalness), Speechiness = ToDecimal(features.Speechiness), Liveness = ToDecimal(features.Liveness), Loudness = ToDecimal(features.Loudness),
        Key = features.Key, Mode = features.Mode, TimeSignature = features.TimeSignature,
    };

    private static decimal? ToDecimal(double? value) => value.HasValue ? (decimal)value.Value : null;
    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
