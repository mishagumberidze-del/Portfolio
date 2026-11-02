using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.Recommendations;

namespace SpotifyTaste.Infrastructure.Analytics;

// Explicit local export only. No controller, provider, importer, scorer or model rebuild.
public static class PersonalSnapshotExporter
{
    public static async Task ExportAsync(SpotifyTasteDbContext db, AnalyticsService analytics,
        StoryService stories, DiagnosticsService diagnostics, FeedbackService feedback, string output)
    {
        if (File.Exists(output)) throw new IOException("Snapshot exists; choose a new output file to preserve it.");
        db.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var capturedAtUtc = DateTime.UtcNow;
        var story = await stories.GetAsync(null);
        var yearValues = await db.ListeningEvents.Select(e => e.PlayedAtUtc.Year).Distinct().OrderBy(x => x).ToListAsync();
        var ranges = new Dictionary<string, object>();
        var storyMap = new Dictionary<string, object> { ["lifetime"] = story };
        var selections = new List<(string Name, DateTime? From, DateTime? To)> { ("lifetime", null, null) };
        selections.AddRange(new[] { 365, 90, 30 }.Select(days => ($"last{days}", (DateTime?)capturedAtUtc.AddDays(-days), (DateTime?)null)));
        selections.AddRange(yearValues.Select(y => ($"year:{y}", (DateTime?)new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc), (DateTime?)new DateTime(y + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
        foreach (var selection in selections)
        {
            ranges[selection.Name] = new {
                overview = await analytics.GetOverviewAsync(selection.From, selection.To),
                behavior = await analytics.GetBehaviorAsync(selection.From, selection.To),
                tracks = await analytics.GetTopTracksAsync(selection.From, selection.To, 500),
                artists = await analytics.GetTopArtistsAsync(selection.From, selection.To, 500),
                albums = await analytics.GetTopAlbumsAsync(selection.From, selection.To, 500)
            };
            Console.WriteLine($"Read stored analytics: {selection.Name}");
        }
        foreach (var year in yearValues) storyMap[year.ToString()] = await stories.GetAsync(year);
        var evolution = new Dictionary<string, object>();
        foreach (var grain in new[] { "year", "quarter", "month" }) evolution[grain] = await analytics.GetArtistEvolutionAsync(grain);
        var stats = await db.ListeningEvents.GroupBy(e => e.TrackId).Select(g => new {
            TrackId = g.Key, Events = g.Count(), TotalMs = g.Sum(e => (long)e.MsPlayed),
            FirstListenUtc = g.Min(e => e.PlayedAtUtc), LastListenUtc = g.Max(e => e.PlayedAtUtc)
        }).ToDictionaryAsync(x => x.TrackId);
        var saved = await db.UserSavedTracks.ToDictionaryAsync(x => x.TrackId);
        var features = (await db.TrackFeatures.OrderBy(x => x.Id).ToListAsync()).GroupBy(x => x.TrackId).ToDictionary(g => g.Key, g => g.ToArray());
        var preferences = (await db.TrackPreferences.OrderByDescending(x => x.CalculatedAtUtc).ThenByDescending(x => x.Id).ToListAsync()).GroupBy(x => x.TrackId).ToDictionary(g => g.Key, g => g.First());
        var tracks = await db.Tracks.OrderBy(x => x.Id).Select(x => new {
            x.Id, x.SpotifyTrackId, x.Name, Album = x.Album == null ? null : x.Album.Name,
            x.DurationMs, x.Isrc, x.ReleaseDate,
            Artists = x.TrackArtists.OrderBy(a => a.ArtistOrder).Select(a => a.Artist.Name).ToArray()
        }).ToListAsync();
        var catalog = tracks.Select(t => new {
            t.Id, t.SpotifyTrackId, t.Name, t.Album, t.DurationMs, t.Isrc, t.ReleaseDate, t.Artists,
            Saved = saved.ContainsKey(t.Id), SavedAtUtc = saved.GetValueOrDefault(t.Id)?.SavedAtUtc,
            History = stats.GetValueOrDefault(t.Id),
            Features = features.GetValueOrDefault(t.Id)?.Select(f => new {
                f.Provider, f.Tempo, f.Energy, f.Danceability, f.Valence, f.Acousticness,
                f.Instrumentalness, f.Speechiness, f.Liveness, f.Loudness, f.Key, f.Mode, f.TimeSignature, f.FetchedAtUtc
            }),
            Preference = preferences.GetValueOrDefault(t.Id) is {} p ? new {
                p.ModelVersion, p.BehavioralScore, p.PositiveWeight, p.NegativeWeight, p.RecencyWeight,
                p.RepeatWeight, p.CompletionWeight, p.SkipPenalty, p.SavedBoost, p.CalculatedAtUtc
            } : null
        }).ToArray();
        // Hash the ordered immutable import identities, not emit raw events into the frontend.
        var factHashes = await db.ListeningEvents.OrderBy(x => x.Id).Select(x => x.ExternalEventHash).ToArrayAsync();
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new SnapshotUtcConverter());
        var factHash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(factHashes, options)));
        var diagnostic = await diagnostics.GetAsync();
        var payload = new {
            Manifest = new { SchemaVersion = 1, Source = "Personal SQL snapshot", CapturedAtUtc = capturedAtUtc,
                Events = stats.Values.Sum(x => x.Events), TotalMs = stats.Values.Sum(x => x.TotalMs),
                SavedTracks = saved.Count, CatalogTracks = catalog.Length, FeatureRows = features.Values.Sum(x => x.Length),
                FactIdentitySha256 = factHash, Years = yearValues, RankingLimit = 500,
                SourceFirstUtc = stats.Values.Min(x => x.FirstListenUtc), SourceLastUtc = stats.Values.Max(x => x.LastListenUtc)
            },
            Ranges = ranges, Stories = storyMap, Evolution = evolution,
            Timeline = await analytics.GetTimelineAsync(), Yearly = await analytics.GetYearlyTimelineAsync(),
            Diagnostics = diagnostic with { SpotifyDataFiles = [], DatabaseName = "Personal SQL snapshot" },
            Discover = await feedback.LatestAsync(), Evaluation = await feedback.GetAsync(), Catalog = catalog
        };
        if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Export attempted a tracked data change.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, options);
        await transaction.RollbackAsync(); // Read-only: never commit anything to SQL.
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write)) await stream.WriteAsync(bytes);
        Console.WriteLine($"Personal snapshot exported: {bytes.Length:N0} bytes. SHA256 {Convert.ToHexString(SHA256.HashData(bytes))}. No database writes.");
    }

    private sealed class SnapshotUtcConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
