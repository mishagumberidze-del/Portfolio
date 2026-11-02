using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Infrastructure.Analytics;

public sealed record StoryEvent(long TrackId, DateTime At, int Ms, bool? Skipped);

// One bounded local snapshot. It never crosses the API boundary and never changes facts.
public sealed class StoryCache
{
    internal SemaphoreSlim Gate { get; } = new(1, 1);
    internal StoryBuilder? Snapshot { get; set; }
    internal DateTime ExpiresUtc { get; set; }
}

public sealed class StoryService(SpotifyTasteDbContext db, StoryCache cache, IOptions<PreferenceModelOptions> options) : IStoryService
{
    public async Task<StoryDto> GetAsync(int? year, CancellationToken cancellationToken = default)
    {
        if (year is < 1900 or > 9998) throw new ArgumentOutOfRangeException(nameof(year));
        await cache.Gate.WaitAsync(cancellationToken);
        try
        {
            if (cache.Snapshot is null || cache.ExpiresUtc <= DateTime.UtcNow)
            {
                var events = await db.ListeningEvents.AsNoTracking()
                    .Select(x => new StoryEvent(x.TrackId, x.PlayedAtUtc, x.MsPlayed, x.WasSkipped)).ToListAsync(cancellationToken);
                var ids = db.ListeningEvents.Select(x => x.TrackId).Distinct();
                var tracks = await db.Tracks.AsNoTracking().Where(x => ids.Contains(x.Id))
                    .Include(x => x.Album).Include(x => x.TrackArtists).ThenInclude(x => x.Artist).AsSplitQuery().ToListAsync(cancellationToken);
                var features = await db.TrackFeatures.AsNoTracking().Where(x => x.Provider == "ReccoBeats" && ids.Contains(x.TrackId)).ToListAsync(cancellationToken);
                cache.Snapshot = new StoryBuilder(events, tracks, features, options.Value);
                cache.ExpiresUtc = DateTime.UtcNow.AddMinutes(2);
            }
            return cache.Snapshot.Build(year);
        }
        finally { cache.Gate.Release(); }
    }
}

public sealed class StoryBuilder
{
    private readonly IReadOnlyList<StoryEvent> all;
    private readonly Dictionary<long, Track> tracks;
    private readonly Dictionary<long, TrackFeature> features;
    private readonly Dictionary<long, int> recordings;
    private readonly Dictionary<int, DateTime> firstRecording;
    private readonly Dictionary<int, int> recordingYears;
    private readonly Dictionary<long, DateTime> firstArtist;
    private readonly PreferenceModelOptions options;
    private readonly Dictionary<int, StoryDto> results = [];
    private static readonly (string Name, string Unit, Func<TrackFeature, decimal?> Get)[] Axes =
    [ ("Energy", "/100", f => f.Energy), ("Danceability", "/100", f => f.Danceability),
      ("Acousticness", "/100", f => f.Acousticness), ("Instrumentalness", "/100", f => f.Instrumentalness),
      ("Valence", "/100", f => f.Valence), ("Tempo", "BPM", f => f.Tempo) ];

    public StoryBuilder(IReadOnlyList<StoryEvent> events, IReadOnlyList<Track> catalog, IReadOnlyList<TrackFeature> audio, PreferenceModelOptions options)
    {
        all = events; tracks = catalog.ToDictionary(x => x.Id); features = audio.ToDictionary(x => x.TrackId); this.options = options;
        var clusters = RecordingIdentityResolver.Cluster(catalog.Select(t => new RecordingIdentityInput(t.Isrc, t.SpotifyTrackId,
            t.Name, t.TrackArtists.OrderBy(a => a.ArtistOrder).Select(a => a.Artist.Name).ToArray(), t.Album?.Name, t.DurationMs)).ToArray());
        recordings = catalog.Select((t, i) => (t.Id, Cluster: clusters[i])).ToDictionary(x => x.Id, x => x.Cluster);
        firstRecording = all.GroupBy(e => recordings[e.TrackId]).ToDictionary(g => g.Key, g => g.Min(e => e.At));
        recordingYears = all.GroupBy(e => recordings[e.TrackId]).ToDictionary(g => g.Key, g => g.Select(e => e.At.Year).Distinct().Count());
        firstArtist = all.SelectMany(e => tracks[e.TrackId].TrackArtists.Select(a => (a.ArtistId, e.At)))
            .GroupBy(x => x.ArtistId).ToDictionary(g => g.Key, g => g.Min(x => x.At));
    }

    public StoryDto Build(int? year)
    {
        if (year is < 1900 or > 9998) throw new ArgumentOutOfRangeException(nameof(year));
        if (results.TryGetValue(year ?? 0, out var cached)) return cached;
        var rows = all.Where(e => !year.HasValue || e.At.Year == year).ToArray();
        var previous = year.HasValue ? all.Where(e => e.At.Year == year - 1).ToArray() : [];
        var artists = RankArtists(rows); var oldArtists = RankArtists(previous);
        var songs = RankSongs(rows); var oldSongs = RankSongs(previous);
        var ms = rows.Sum(e => (long)e.Ms);
        var summary = new StorySummary(ms, rows.Length, rows.Count(Qualified), rows.Select(e => e.At.Date).Distinct().Count(),
            rows.Select(e => recordings[e.TrackId]).Distinct().Count(), rows.SelectMany(e => tracks[e.TrackId].TrackArtists.Select(a => a.ArtistId)).Distinct().Count(),
            rows.Select(e => tracks[e.TrackId].AlbumId).Where(id => id.HasValue).Distinct().Count(),
            rows.Length == 0 ? null : rows.Min(e => e.At), rows.Length == 0 ? null : rows.Max(e => e.At));
        var albums = rows.Where(e => tracks[e.TrackId].AlbumId.HasValue).GroupBy(e => tracks[e.TrackId].AlbumId!.Value)
            .Select(g => Ranking(g.Key, tracks[g.First().TrackId].Album!.Name, g.ToArray(), "album")).OrderByDescending(x => x.TotalMs).ThenByDescending(x => x.QualifiedPlays).ThenBy(x => x.Id).Take(5).ToArray();
        var months = rows.GroupBy(e => e.At.ToString("yyyy-MM")).ToDictionary(g => g.Key,
            g => new StoryMonth(g.Key, g.Sum(e => (long)e.Ms), RankArtists(g.ToArray()).FirstOrDefault()?.Name));
        var journey = year.HasValue ? Enumerable.Range(1, 12).Select(m => $"{year:0000}-{m:00}").Select(p => months.GetValueOrDefault(p) ?? new StoryMonth(p, 0, null)).ToArray() : months.Values.OrderBy(m => m.Period).ToArray();
        var known = rows.Where(e => tracks[e.TrackId].DurationMs is > 0).ToArray();
        var behavior = new BehaviorAnalyticsDto(rows.Length, Percent(rows.Count(e => e.Skipped == true || e.Ms < 10_000), rows.Length),
            Percent(rows.Count(e => e.Ms < 10_000), rows.Length), Percent(rows.Length - summary.Recordings, rows.Length),
            known.Length == 0 ? null : Math.Round((decimal)known.Average(e => Math.Clamp(100d * e.Ms / tracks[e.TrackId].DurationMs!.Value, 0, 125)), 1),
            known.Length == 0 ? null : Percent(known.Count(e => e.Ms >= tracks[e.TrackId].DurationMs!.Value * .9), known.Length), known.Length);
        var years = all.Select(e => e.At.Year).Distinct().OrderDescending().ToArray();
        var eras = years.Order().Select(y => new SoundEra(y, Profile(all.Where(e => e.At.Year == y).ToArray()))).ToArray();
        var facts = new List<StoryFact>();
        var recurring = rows.GroupBy(e => recordings[e.TrackId]).Where(g => g.Count(Qualified) >= 3)
            .Select(g => new { Rows = g.ToArray(), Years = recordingYears[g.Key], First = firstRecording[g.Key] })
            .OrderByDescending(x => x.Years).ThenBy(x => x.First).FirstOrDefault();
        if (recurring is not null) facts.Add(new("A returning favorite", $"{tracks[recurring.Rows[0].TrackId].Name} appears in {recurring.Years} calendar years of your imported history, first recorded on {recurring.First:yyyy-MM-dd}."));
        var newcomer = artists.Take(10).FirstOrDefault(a => year.HasValue && firstArtist[a.Id].Year == year);
        if (newcomer is not null) facts.Add(new("New to your rotation", $"{newcomer.Name} first appears in your imported history in {year} and reached #{artists.IndexOf(newcomer) + 1} for the year by listening time."));
        var busiest = journey.OrderByDescending(m => m.TotalMs).FirstOrDefault(m => m.TotalMs > 0);
        if (busiest is not null) facts.Add(new("Your loudest chapter", $"{busiest.Period} had the most listening in this selection: {busiest.TotalMs / 3_600_000d:N1} hours. Its leading artist was {busiest.DefiningArtist ?? "unknown"}."));
        var profile = Profile(rows); var insights = new List<string>();
        if (previous.Length > 0)
        {
            var old = Profile(previous);
            for (var i = 0; i < profile.Count; i++)
                if (profile[i].Value is double value && old[i].Value is double prior && profile[i].CoveragePercent >= 50 && old[i].CoveragePercent >= 50 && Math.Abs(value - prior) >= 5)
                    insights.Add($"The music you listened to had {(value > prior ? "higher" : "lower")} listening-time-weighted {profile[i].Name.ToLowerInvariant()} than {year - 1}: {prior:0.0} → {value:0.0} {profile[i].Unit}.");
        }
        StoryComparison? comparison = previous.Length == 0 ? null : new(year!.Value - 1, previous.Sum(e => (long)e.Ms),
            previous.Sum(e => (long)e.Ms) == 0 ? null : Math.Round(100d * (ms - previous.Sum(e => (long)e.Ms)) / previous.Sum(e => (long)e.Ms), 1),
            "Imported calendar-year totals, not annualized. First/last export years may be partial; absence of plays is not evidence of missing export data.");
        var dto = new StoryDto(year, years, summary, WithPrevious(artists, oldArtists), WithPrevious(songs, oldSongs), albums, journey,
            rows.Select(e => recordings[e.TrackId]).Distinct().Count(id => !year.HasValue || firstRecording[id].Year == year),
            rows.SelectMany(e => tracks[e.TrackId].TrackArtists.Select(a => a.ArtistId)).Distinct().Count(id => !year.HasValue || firstArtist[id].Year == year),
            behavior, profile, eras, comparison, facts, insights);
        results[year ?? 0] = dto; return dto;
    }

    private List<StoryRanking> RankArtists(IReadOnlyList<StoryEvent> rows) => rows.SelectMany(e => tracks[e.TrackId].TrackArtists.Select(a => (a.ArtistId, a.Artist.Name, Event: e)))
        .GroupBy(x => new { x.ArtistId, x.Name }).Select(g => Ranking(g.Key.ArtistId, g.Key.Name, g.Select(x => x.Event).ToArray(), "artist"))
        .OrderByDescending(x => x.TotalMs).ThenByDescending(x => x.QualifiedPlays).ThenBy(x => x.Id).ToList();
    private List<StoryRanking> RankSongs(IReadOnlyList<StoryEvent> rows) => rows.GroupBy(e => recordings[e.TrackId])
        .Select(g => { var rep = g.GroupBy(e => e.TrackId).OrderByDescending(t => t.Sum(e => (long)e.Ms)).ThenBy(t => t.Key).First().Key;
            return Ranking(g.Key, tracks[rep].Name, g.ToArray(), "song", rep) with { FirstListenUtc = firstRecording[g.Key] }; })
        .OrderByDescending(x => x.TotalMs).ThenByDescending(x => x.QualifiedPlays).ThenBy(x => x.Id).ToList();
    private StoryRanking Ranking(long id, string name, StoryEvent[] rows, string kind, long? representative = null)
    {
        var track = tracks[representative ?? rows.GroupBy(e => e.TrackId).OrderByDescending(g => g.Sum(e => (long)e.Ms)).ThenBy(g => g.Key).First().Key];
        var spotifyId = kind switch { "song" => track.SpotifyTrackId, "album" => track.Album?.SpotifyAlbumId, "artist" => track.TrackArtists.FirstOrDefault(a => a.ArtistId == id)?.Artist.SpotifyArtistId, _ => null };
        var spotifyKind = kind == "song" ? "track" : kind;
        var spotifyUrl = spotifyId is { Length: 22 } && spotifyId.All(c => char.IsAsciiLetterOrDigit(c))
            ? $"https://open.spotify.com/{spotifyKind}/{spotifyId}" : null;
        return new(id, name, kind == "artist" ? null : string.Join(", ", track.TrackArtists.OrderBy(a => a.ArtistOrder).Select(a => a.Artist.Name)),
            kind == "song" ? track.Album?.Name : null, track.Album?.ImageUrl, rows.Sum(e => (long)e.Ms), rows.Length, rows.Count(Qualified),
            rows.Min(e => e.At), rows.GroupBy(e => e.At.ToString("yyyy-MM")).OrderByDescending(g => g.Sum(e => (long)e.Ms)).ThenBy(g => g.Key).First().Key, null, spotifyUrl);
    }
    private IReadOnlyList<SoundAxis> Profile(IReadOnlyList<StoryEvent> rows)
    {
        var weights = rows.GroupBy(e => e.TrackId).ToDictionary(g => g.Key, g => g.Sum(e => (long)e.Ms));
        var lifetime = all.GroupBy(e => e.TrackId).ToDictionary(g => g.Key, g => g.Sum(e => (long)e.Ms));
        return Axes.Select(axis =>
        {
            bool Valid(decimal? v) => v.HasValue && (axis.Unit == "BPM" ? v is > 0 and <= 400 : v is >= 0 and <= 1);
            var points = weights.Where(w => w.Value > 0 && features.TryGetValue(w.Key, out var f) && Valid(axis.Get(f))).Select(w => (Value: (double)axis.Get(features[w.Key])!.Value, Weight: w.Value)).ToArray();
            var mass = points.Sum(p => p.Weight); double? mean = mass == 0 ? null : points.Sum(p => p.Value * p.Weight) / mass;
            var reference = lifetime.Where(w => w.Value > 0 && features.TryGetValue(w.Key, out var f) && Valid(axis.Get(f))).Select(w => (Value: (double)axis.Get(features[w.Key])!.Value, Weight: w.Value)).ToArray();
            double? percentile = mean is null || reference.Length == 0 ? null : 100d * reference.Where(p => p.Value < mean).Sum(p => p.Weight) / reference.Sum(p => p.Weight);
            return new SoundAxis(axis.Name, mean.HasValue ? Math.Round(mean.Value * (axis.Unit == "BPM" ? 1 : 100), 1) : null, axis.Unit,
                weights.Values.Sum() == 0 ? 0 : Math.Round(100d * mass / weights.Values.Sum(), 1), percentile is null ? null : Math.Round(percentile.Value, 1));
        }).ToArray();
    }
    private bool Qualified(StoryEvent e) => e.Ms >= options.QualifiedPlayMs || (tracks[e.TrackId].DurationMs is > 0 && e.Ms >= tracks[e.TrackId].DurationMs * options.QualifiedDurationFraction);
    private static decimal Percent(int n, int d) => d == 0 ? 0 : Math.Round(100m * n / d, 1);
    private static StoryRanking[] WithPrevious(List<StoryRanking> items, List<StoryRanking> previous) => items.Take(5).Select(x => x with { PreviousRank = previous.FindIndex(p => p.Id == x.Id) is var rank && rank >= 0 ? rank + 1 : null }).ToArray();
}
