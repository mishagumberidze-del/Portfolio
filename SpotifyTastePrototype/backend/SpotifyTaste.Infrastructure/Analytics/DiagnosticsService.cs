using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Infrastructure.Analytics;

public sealed class DiagnosticsService(SpotifyTasteDbContext dbContext, IOptions<PreferenceModelOptions>? configuredOptions = null) : IDiagnosticsService
{
    private readonly PreferenceModelOptions options = configuredOptions?.Value ?? new PreferenceModelOptions();
    public async Task<DiagnosticsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var connection = dbContext.Database.GetDbConnection();
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var files = await dbContext.ImportBatches.AsNoTracking().OrderByDescending(x => x.StartedAtUtc).Select(x => x.FileName).Distinct().Take(20).ToListAsync(cancellationToken);
        var historyTrackIds = dbContext.ListeningEvents.Select(x => x.TrackId).Distinct();
        var featureTrackIds = dbContext.TrackFeatures.Where(x => x.Provider == "ReccoBeats" && historyTrackIds.Contains(x.TrackId)).Select(x => x.TrackId);
        var recordingRows = await dbContext.Tracks.AsNoTracking().Where(x => historyTrackIds.Contains(x.Id)).Select(x => new
        {
            x.SpotifyTrackId,
            x.Isrc,
            x.Name,
            Album = x.Album != null ? x.Album.Name : null,
            x.DurationMs,
            Artists = x.TrackArtists.OrderBy(ta => ta.ArtistOrder).Select(ta => ta.Artist.Name).ToArray(),
        }).ToListAsync(cancellationToken);
        var recordingInputs = recordingRows.Select(x => new RecordingIdentityInput(x.Isrc, x.SpotifyTrackId, x.Name, x.Artists, x.Album, x.DurationMs)).ToArray();
        var uniqueRecordings = RecordingIdentityResolver.Cluster(recordingInputs).Distinct().Count();
        return new DiagnosticsDto(
            dbContext.Database.ProviderName ?? "Unknown",
            connection.Database,
            files,
            await dbContext.ListeningEvents.CountAsync(cancellationToken),
            user?.SpotifyLibraryDataAvailable ?? false,
            await dbContext.UserSavedTracks.CountAsync(cancellationToken),
            uniqueRecordings,
            await dbContext.ListeningEvents.SelectMany(x => x.Track.TrackArtists.Select(ta => ta.ArtistId)).Distinct().CountAsync(cancellationToken),
            await dbContext.ListeningEvents.Where(x => x.Track.AlbumId != null).Select(x => x.Track.AlbumId).Distinct().CountAsync(cancellationToken),
            await dbContext.Tracks.CountAsync(x => x.SpotifyTrackId != null && historyTrackIds.Contains(x.Id), cancellationToken),
            await dbContext.TrackExternalIds.CountAsync(x => x.Provider == "ReccoBeats" && historyTrackIds.Contains(x.TrackId), cancellationToken),
            await featureTrackIds.Distinct().CountAsync(cancellationToken),
            await dbContext.Tracks.CountAsync(x => x.SpotifyTrackId != null && historyTrackIds.Contains(x.Id) && !featureTrackIds.Contains(x.Id), cancellationToken),
            await dbContext.Tracks.CountAsync(x => historyTrackIds.Contains(x.Id) && x.EnrichmentStatus == EnrichmentStatus.NotFound, cancellationToken),
            options.ModelVersion,
            "ReccoBeats",
            "recommendation-v4-feedback-suppression");
    }
}
