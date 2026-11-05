using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Infrastructure.Analytics;

public sealed class AnalyticsService(SpotifyTasteDbContext dbContext, IOptions<PreferenceModelOptions>? configuredOptions = null) : IAnalyticsService
{
    private readonly PreferenceModelOptions options = configuredOptions?.Value ?? new PreferenceModelOptions();

    public async Task<OverviewDto> GetOverviewAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var query = Filter(fromUtc, toUtc);
        var aggregate = await query.GroupBy(_ => 1).Select(group => new
        {
            TotalMs = group.Sum(x => (long)x.MsPlayed),
            Events = group.Count(),
            Qualified = group.Count(x => x.MsPlayed >= options.QualifiedPlayMs || (x.Track.DurationMs != null && x.Track.DurationMs > 0 && x.MsPlayed >= x.Track.DurationMs * options.QualifiedDurationFraction)),
            Tracks = group.Select(x => x.TrackId).Distinct().Count(),
            Albums = group.Where(x => x.Track.AlbumId != null).Select(x => x.Track.AlbumId).Distinct().Count(),
            First = group.Min(x => x.PlayedAtUtc),
            Last = group.Max(x => x.PlayedAtUtc),
        }).SingleOrDefaultAsync(cancellationToken);

        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        var artists = await query.SelectMany(x => x.Track.TrackArtists.Select(ta => ta.ArtistId)).Distinct().CountAsync(cancellationToken);
        var days = await query.Select(x => x.PlayedAtUtc.Date).Distinct().CountAsync(cancellationToken);
        var logicalTracks = (await LoadTrackIdentitiesAsync(query, cancellationToken)).Select(x => x.RecordingKey).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        int? savedCount = user?.SpotifyLibraryDataAvailable == true ? await dbContext.UserSavedTracks.CountAsync(cancellationToken) : null;
        var ms = aggregate?.TotalMs ?? 0;
        return new OverviewDto(ms / 60_000, Math.Round(ms / 3_600_000m, 1), days, aggregate?.Events ?? 0, aggregate?.Qualified ?? 0, logicalTracks, artists, aggregate?.Albums ?? 0, user?.SpotifyLibraryDataAvailable ?? false, savedCount, aggregate?.First, aggregate?.Last);
    }

    public async Task<IReadOnlyList<TopTrackDto>> GetTopTracksAsync(DateTime? fromUtc, DateTime? toUtc, int limit, CancellationToken cancellationToken = default)
    {
        var libraryAvailable = await dbContext.Users.Select(x => x.SpotifyLibraryDataAvailable).SingleOrDefaultAsync(cancellationToken);
        var savedIds = libraryAvailable ? await dbContext.UserSavedTracks.Select(x => x.TrackId).ToListAsync(cancellationToken) : [];
        var saved = savedIds.ToHashSet();
        var query = Filter(fromUtc, toUtc);
        var identityByTrack = (await LoadTrackIdentitiesAsync(query, cancellationToken, savedIds)).ToDictionary(x => x.TrackId);
        var savedRecordings = identityByTrack.Values.Where(x => saved.Contains(x.TrackId)).Select(x => x.RecordingKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rows = await query.GroupBy(x => new { x.TrackId, x.Track.Name, Album = x.Track.Album != null ? x.Track.Album.Name : null })
            .Select(group => new
            {
                group.Key.TrackId, Track = group.Key.Name, group.Key.Album,
                Artist = group.SelectMany(x => x.Track.TrackArtists).OrderBy(x => x.ArtistOrder).Select(x => x.Artist.Name).FirstOrDefault() ?? "Unknown Artist",
                RawEvents = group.Count(),
                Qualified = group.Count(x => x.MsPlayed >= options.QualifiedPlayMs || (x.Track.DurationMs != null && x.Track.DurationMs > 0 && x.MsPlayed >= x.Track.DurationMs * options.QualifiedDurationFraction)),
                TotalMs = group.Sum(x => (long)x.MsPlayed),
                AverageMs = group.Average(x => (double)x.MsPlayed),
                Skips = group.Count(x => x.WasSkipped == true || x.MsPlayed < 10_000),
                First = group.Min(x => x.PlayedAtUtc), Last = group.Max(x => x.PlayedAtUtc),
            })
            .ToListAsync(cancellationToken);

        var grouped = rows.GroupBy(x => identityByTrack.GetValueOrDefault(x.TrackId)?.RecordingKey ?? $"track:{x.TrackId}", StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var representative = group.OrderByDescending(x => x.TotalMs).ThenByDescending(x => x.Qualified).First();
                var rawEvents = group.Sum(x => x.RawEvents);
                var totalMs = group.Sum(x => x.TotalMs);
                return new
                {
                    representative.TrackId,
                    representative.Track,
                    representative.Artist,
                    representative.Album,
                    RawEvents = rawEvents,
                    Qualified = group.Sum(x => x.Qualified),
                    TotalMs = totalMs,
                    AverageMs = rawEvents == 0 ? 0 : totalMs / (double)rawEvents,
                    Skips = group.Sum(x => x.Skips),
                    First = group.Min(x => x.First),
                    Last = group.Max(x => x.Last),
                    Saved = savedRecordings.Contains(group.Key),
                };
            })
            .OrderByDescending(x => x.TotalMs).ThenByDescending(x => x.Qualified)
            .Take(Math.Clamp(limit, 1, 500));

        return grouped.Select(x => new TopTrackDto(x.TrackId, x.Track, x.Artist, x.Album, x.RawEvents, x.Qualified, Math.Round(x.TotalMs / 60_000m, 1), Math.Round((decimal)x.AverageMs / 1000, 1), Math.Round(100m * x.Skips / Math.Max(1, x.RawEvents), 1), x.First, x.Last, libraryAvailable ? x.Saved : null)).ToList();
    }

    public async Task<IReadOnlyList<TopArtistDto>> GetTopArtistsAsync(DateTime? fromUtc, DateTime? toUtc, int limit, CancellationToken cancellationToken = default)
    {
        var query = Filter(fromUtc, toUtc);
        var identities = (await LoadTrackIdentitiesAsync(query, cancellationToken)).ToDictionary(x => x.TrackId, x => x.RecordingKey);
        var artistTrackPairs = await query.SelectMany(x => x.Track.TrackArtists.Select(ta => new { ta.ArtistId, x.TrackId })).Distinct().ToListAsync(cancellationToken);
        var logicalTrackCounts = artistTrackPairs.GroupBy(x => x.ArtistId).ToDictionary(
            group => group.Key,
            group => group.Select(x => identities.GetValueOrDefault(x.TrackId) ?? $"track:{x.TrackId}").Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var rows = await query.SelectMany(x => x.Track.TrackArtists.Select(ta => new
            {
                x.TrackId, x.MsPlayed, x.WasSkipped, x.PlayedAtUtc, x.Track.DurationMs,
                ta.ArtistId, ta.Artist.Name,
            }))
            .GroupBy(x => new { x.ArtistId, Artist = x.Name })
            .Select(group => new
            {
                group.Key.ArtistId, group.Key.Artist,
                TotalMs = group.Sum(x => (long)x.MsPlayed),
                Qualified = group.Count(x => x.MsPlayed >= options.QualifiedPlayMs || (x.DurationMs != null && x.DurationMs > 0 && x.MsPlayed >= x.DurationMs * options.QualifiedDurationFraction)),
                Raw = group.Count(), Tracks = group.Select(x => x.TrackId).Distinct().Count(),
                Skips = group.Count(x => x.WasSkipped == true || x.MsPlayed < 10_000),
                First = group.Min(x => x.PlayedAtUtc), Last = group.Max(x => x.PlayedAtUtc),
            }).OrderByDescending(x => x.TotalMs).ThenByDescending(x => x.Qualified).Take(Math.Clamp(limit, 1, 500)).ToListAsync(cancellationToken);
        return rows.Select(x => new TopArtistDto(x.ArtistId, x.Artist, Math.Round(x.TotalMs / 60_000m, 1), x.Qualified, x.Raw, logicalTrackCounts.GetValueOrDefault(x.ArtistId, x.Tracks), Math.Round(100m * x.Skips / Math.Max(1, x.Raw), 1), x.First, x.Last)).ToList();
    }

    public async Task<IReadOnlyList<TopAlbumDto>> GetTopAlbumsAsync(DateTime? fromUtc, DateTime? toUtc, int limit, CancellationToken cancellationToken = default)
    {
        var query = Filter(fromUtc, toUtc);
        var identities = (await LoadTrackIdentitiesAsync(query, cancellationToken)).ToDictionary(x => x.TrackId, x => x.RecordingKey);
        var albumTrackPairs = await query.Where(x => x.Track.AlbumId != null).Select(x => new { AlbumId = x.Track.AlbumId!.Value, x.TrackId }).Distinct().ToListAsync(cancellationToken);
        var logicalTrackCounts = albumTrackPairs.GroupBy(x => x.AlbumId).ToDictionary(
            group => group.Key,
            group => group.Select(x => identities.GetValueOrDefault(x.TrackId) ?? $"track:{x.TrackId}").Distinct(StringComparer.OrdinalIgnoreCase).Count());
        var rows = await query.Where(x => x.Track.AlbumId != null).GroupBy(x => new { AlbumId = x.Track.AlbumId!.Value, Album = x.Track.Album!.Name, Artist = x.Track.Album.PrimaryArtist != null ? x.Track.Album.PrimaryArtist.Name : "Unknown Artist" })
            .Select(group => new { group.Key.AlbumId, group.Key.Album, group.Key.Artist, TotalMs = group.Sum(x => (long)x.MsPlayed), Qualified = group.Count(x => x.MsPlayed >= options.QualifiedPlayMs || (x.Track.DurationMs != null && x.Track.DurationMs > 0 && x.MsPlayed >= x.Track.DurationMs * options.QualifiedDurationFraction)), Tracks = group.Select(x => x.TrackId).Distinct().Count() })
            .OrderByDescending(x => x.TotalMs).ThenByDescending(x => x.Qualified).Take(Math.Clamp(limit, 1, 500)).ToListAsync(cancellationToken);
        return rows.Select(x => new TopAlbumDto(x.AlbumId, x.Album, x.Artist, Math.Round(x.TotalMs / 60_000m, 1), x.Qualified, logicalTrackCounts.GetValueOrDefault(x.AlbumId, x.Tracks))).ToList();
    }

    public async Task<IReadOnlyList<TimelinePointDto>> GetTimelineAsync(CancellationToken cancellationToken = default)
    {
        var identityByTrack = (await LoadTrackIdentitiesAsync(dbContext.ListeningEvents.AsNoTracking(), cancellationToken)).ToDictionary(x => x.TrackId, x => x.RecordingKey);
        var rows = await dbContext.ListeningEvents.AsNoTracking().Select(x => new { x.PlayedAtUtc, x.MsPlayed, x.TrackId, ArtistIds = x.Track.TrackArtists.Select(ta => ta.ArtistId).ToArray() }).ToListAsync(cancellationToken);
        var firstTrack = rows.GroupBy(x => identityByTrack.GetValueOrDefault(x.TrackId) ?? $"track:{x.TrackId}", StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Min(v => v.PlayedAtUtc), StringComparer.OrdinalIgnoreCase);
        var firstArtist = rows.SelectMany(x => x.ArtistIds.Select(id => new { Id = id, x.PlayedAtUtc })).GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.Min(v => v.PlayedAtUtc));
        return rows.GroupBy(x => $"{x.PlayedAtUtc:yyyy-MM}").OrderBy(x => x.Key).Select(group => new TimelinePointDto(
            group.Key,
            Math.Round(group.Sum(x => (long)x.MsPlayed) / 60_000m, 1),
            group.SelectMany(x => x.ArtistIds).Distinct().Count(),
            group.SelectMany(x => x.ArtistIds).Distinct().Count(id => firstArtist[id].ToString("yyyy-MM") == group.Key),
            group.Select(x => identityByTrack.GetValueOrDefault(x.TrackId) ?? $"track:{x.TrackId}").Distinct(StringComparer.OrdinalIgnoreCase).Count(id => firstTrack[id].ToString("yyyy-MM") == group.Key))).ToList();
    }

    public async Task<IReadOnlyList<TimelinePointDto>> GetYearlyTimelineAsync(CancellationToken cancellationToken = default)
    {
        var identityByTrack = (await LoadTrackIdentitiesAsync(dbContext.ListeningEvents.AsNoTracking(), cancellationToken)).ToDictionary(x => x.TrackId, x => x.RecordingKey);
        var rows = await dbContext.ListeningEvents.AsNoTracking().Select(x => new { x.PlayedAtUtc, x.MsPlayed, x.TrackId, ArtistIds = x.Track.TrackArtists.Select(ta => ta.ArtistId).ToArray() }).ToListAsync(cancellationToken);
        var firstTrack = rows.GroupBy(x => identityByTrack.GetValueOrDefault(x.TrackId) ?? $"track:{x.TrackId}", StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Min(v => v.PlayedAtUtc), StringComparer.OrdinalIgnoreCase);
        var firstArtist = rows.SelectMany(x => x.ArtistIds.Select(id => new { Id = id, x.PlayedAtUtc })).GroupBy(x => x.Id).ToDictionary(x => x.Key, x => x.Min(v => v.PlayedAtUtc));
        return rows.GroupBy(x => x.PlayedAtUtc.Year).OrderBy(x => x.Key).Select(group => new TimelinePointDto(
            group.Key.ToString(),
            Math.Round(group.Sum(x => (long)x.MsPlayed) / 60_000m, 1),
            group.SelectMany(x => x.ArtistIds).Distinct().Count(),
            group.SelectMany(x => x.ArtistIds).Distinct().Count(id => firstArtist[id].Year == group.Key),
            group.Select(x => identityByTrack.GetValueOrDefault(x.TrackId) ?? $"track:{x.TrackId}").Distinct(StringComparer.OrdinalIgnoreCase).Count(id => firstTrack[id].Year == group.Key))).ToList();
    }

    public async Task<IReadOnlyList<RankEvolutionDto>> GetArtistEvolutionAsync(string grain, CancellationToken cancellationToken = default)
    {
        var rows = await dbContext.ListeningEvents.AsNoTracking().SelectMany(x => x.Track.TrackArtists.Select(ta => new
        {
            x.PlayedAtUtc, x.MsPlayed, ta.ArtistId, ta.Artist.Name,
            ArtworkUrl = x.Track.Album != null ? x.Track.Album.ImageUrl : null,
            Qualified = x.MsPlayed >= options.QualifiedPlayMs || (x.Track.DurationMs != null && x.Track.DurationMs > 0 && x.MsPlayed >= x.Track.DurationMs * options.QualifiedDurationFraction),
        })).ToListAsync(cancellationToken);
        // A stable associated album image across periods, never presented as an artist portrait/logo.
        var artwork = rows.Where(x => Uri.TryCreate(x.ArtworkUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo))
            .GroupBy(x => x.ArtistId).ToDictionary(g => g.Key, g => g.GroupBy(x => x.ArtworkUrl!)
                .OrderByDescending(image => image.Sum(x => (long)x.MsPlayed)).ThenBy(image => image.Key, StringComparer.Ordinal).First().Key);
        string Period(DateTime date) => grain.ToLowerInvariant() switch { "year" => date.ToString("yyyy"), "quarter" => $"{date:yyyy}-Q{((date.Month - 1) / 3) + 1}", _ => date.ToString("yyyy-MM") };
        return rows.GroupBy(x => Period(x.PlayedAtUtc)).OrderBy(x => x.Key).SelectMany(period => period.GroupBy(x => new { x.ArtistId, Artist = x.Name }).Select(group => new { group.Key.ArtistId, group.Key.Artist, Minutes = group.Sum(x => (long)x.MsPlayed) / 60_000m, Qualified = group.Count(x => x.Qualified) }).OrderByDescending(x => x.Minutes).ThenByDescending(x => x.Qualified).ThenBy(x => x.ArtistId).Take(20).Select((x, index) => new RankEvolutionDto(period.Key, x.ArtistId, x.Artist, index + 1, Math.Round(x.Minutes, 1), x.Qualified, artwork.GetValueOrDefault(x.ArtistId)))).ToList();
    }

    public async Task<BehaviorAnalyticsDto> GetBehaviorAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default)
    {
        var query = Filter(fromUtc, toUtc);
        var identityByTrack = (await LoadTrackIdentitiesAsync(query, cancellationToken)).ToDictionary(x => x.TrackId, x => x.RecordingKey);
        var rows = await query.Select(x => new { x.TrackId, x.MsPlayed, x.WasSkipped, x.Track.DurationMs }).ToListAsync(cancellationToken);
        if (rows.Count == 0) return new(0, 0, 0, 0, null, null, 0);
        var known = rows.Where(x => x.DurationMs is > 0).ToArray();
        var repeatEvents = rows.GroupBy(x => identityByTrack.GetValueOrDefault(x.TrackId) ?? $"track:{x.TrackId}", StringComparer.OrdinalIgnoreCase).Sum(group => Math.Max(0, group.Count() - 1));
        decimal? averagePercent = known.Length == 0 ? null : Math.Round((decimal)known.Average(x => Math.Clamp(100d * x.MsPlayed / x.DurationMs!.Value, 0, 125)), 1);
        decimal? completion = known.Length == 0 ? null : Math.Round(100m * known.Count(x => x.MsPlayed >= x.DurationMs!.Value * 0.9) / known.Length, 1);
        return new BehaviorAnalyticsDto(
            rows.Count,
            Math.Round(100m * rows.Count(x => x.WasSkipped == true || x.MsPlayed < 10_000) / rows.Count, 1),
            Math.Round(100m * rows.Count(x => x.MsPlayed < 10_000) / rows.Count, 1),
            Math.Round(100m * repeatEvents / rows.Count, 1),
            averagePercent,
            completion,
            known.Length);
    }

    private IQueryable<Domain.ListeningEvent> Filter(DateTime? fromUtc, DateTime? toUtc)
    {
        var query = dbContext.ListeningEvents.AsNoTracking();
        if (fromUtc.HasValue) query = query.Where(x => x.PlayedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue) query = query.Where(x => x.PlayedAtUtc < toUtc.Value);
        return query;
    }

    private async Task<IReadOnlyList<TrackIdentityData>> LoadTrackIdentitiesAsync(IQueryable<Domain.ListeningEvent> events, CancellationToken cancellationToken, IReadOnlyCollection<long>? additionalTrackIds = null)
    {
        additionalTrackIds ??= [];
        var trackIds = events.Select(x => x.TrackId).Distinct();
        var rows = await dbContext.Tracks.AsNoTracking().Where(x => trackIds.Contains(x.Id) || additionalTrackIds.Contains(x.Id)).Select(x => new
        {
            x.Id,
            x.Isrc,
            x.SpotifyTrackId,
            x.Name,
            Album = x.Album != null ? x.Album.Name : null,
            x.DurationMs,
            Artists = x.TrackArtists.OrderBy(ta => ta.ArtistOrder).Select(ta => ta.Artist.Name).ToArray(),
        }).ToListAsync(cancellationToken);

        var inputs = rows.Select(x => new RecordingIdentityInput(x.Isrc, x.SpotifyTrackId, x.Name, x.Artists, x.Album, x.DurationMs)).ToArray();
        var clusters = RecordingIdentityResolver.Cluster(inputs);
        return rows.Select((x, index) => new TrackIdentityData(x.Id, $"recording:{clusters[index]}"))
            .ToList();
    }

    private sealed record TrackIdentityData(long TrackId, string RecordingKey);
}
