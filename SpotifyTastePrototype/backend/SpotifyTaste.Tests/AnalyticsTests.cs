using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Analytics;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Tests;

public sealed class AnalyticsTests
{
    [Fact]
    public async Task Artist_race_uses_stable_associated_artwork_without_altering_period_minutes()
    {
        await using var db = CreateContext();
        var at = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = new User { DisplayName = "Local" };
        var artist = new Artist { Name = "Artist", NormalizedName = "ARTIST" };
        var first = Track(new string('a', 22), "First", new Album { Name = "One", ImageUrl = "https://example.test/one.jpg" }, "USAAA2500001", 200000, artist, at);
        var second = Track(new string('b', 22), "Second", new Album { Name = "Two", ImageUrl = "https://example.test/two.jpg" }, "USAAA2500002", 200000, artist, at);
        db.ListeningEvents.AddRange(Event(user, first, at, '1'), Event(user, second, at.AddMonths(1), '2'), Event(user, second, at.AddMonths(1).AddDays(1), '3'));
        await db.SaveChangesAsync();
        var results = await new AnalyticsService(db).GetArtistEvolutionAsync("month");
        Assert.Equal(2, results.Count);
        Assert.All(results, row => Assert.Equal("https://example.test/two.jpg", row.ArtworkUrl));
        Assert.Equal(3.2m, results[0].ListeningMinutes); Assert.Equal(6.3m, results[1].ListeningMinutes);
        Assert.Equal(3, await db.ListeningEvents.CountAsync());
    }
    [Fact]
    public async Task Artist_race_rejects_unsafe_artwork_and_breaks_equal_ranks_by_identity()
    {
        await using var db = CreateContext();
        var at = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = new User { DisplayName = "Local" };
        var second = Track(new string('c', 22), "Other", new Album { Name = "Other", ImageUrl = "file:///private.png" }, "USAAA2500003", 200000, new Artist { Id = 20, Name = "Second", NormalizedName = "SECOND" }, at);
        var first = Track(new string('d', 22), "First", new Album { Name = "First", ImageUrl = "https://user:password@example.test/cover" }, "USAAA2500004", 200000, new Artist { Id = 10, Name = "First", NormalizedName = "FIRST" }, at);
        db.ListeningEvents.AddRange(Event(user, second, at, '4'), Event(user, first, at, '5'));
        await db.SaveChangesAsync();
        var results = await new AnalyticsService(db).GetArtistEvolutionAsync("year");
        Assert.Equal(new long[] { 10, 20 }, results.Select(row => row.ArtistId));
        Assert.All(results, row => Assert.Null(row.ArtworkUrl));
    }
    [Fact]
    public async Task Overview_and_rankings_use_fact_rows_without_duplicating_metadata()
    {
        await using var db = CreateContext();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = new User { DisplayName = "Local User", CreatedAtUtc = now, UpdatedAtUtc = now };
        var artist = new Artist { Name = "Artist", NormalizedName = "ARTIST", CreatedAtUtc = now, UpdatedAtUtc = now };
        var album = new Album { Name = "Album", NormalizedName = "ALBUM", PrimaryArtist = artist, CreatedAtUtc = now, UpdatedAtUtc = now };
        var track = new Track { Name = "Track", NormalizedName = "TRACK", SpotifyTrackId = "6fOqEAXCwboCO4BdzbZG1K", SpotifyUri = "spotify:track:6fOqEAXCwboCO4BdzbZG1K", Album = album, DurationMs = 200_000, CreatedAtUtc = now, UpdatedAtUtc = now };
        track.TrackArtists.Add(new TrackArtist { Track = track, Artist = artist, ArtistOrder = 0 });
        db.ListeningEvents.AddRange(
            new ListeningEvent { User = user, Track = track, PlayedAtUtc = now, MsPlayed = 180_000, Source = "test", ImportedAtUtc = now, ExternalEventHash = new string('a', 64) },
            new ListeningEvent { User = user, Track = track, PlayedAtUtc = now.AddDays(1), MsPlayed = 20_000, WasSkipped = true, Source = "test", ImportedAtUtc = now, ExternalEventHash = new string('b', 64) });
        await db.SaveChangesAsync();

        var analytics = new AnalyticsService(db);
        var overview = await analytics.GetOverviewAsync(null, null);
        var topTracks = await analytics.GetTopTracksAsync(null, null, 10);
        var topArtists = await analytics.GetTopArtistsAsync(null, null, 10);
        var behavior = await analytics.GetBehaviorAsync(null, null);

        Assert.Equal(2, overview.ListeningEvents);
        Assert.Equal(1, overview.QualifiedPlays);
        Assert.Equal(3, overview.TotalListeningMinutes);
        Assert.Equal("Track", Assert.Single(topTracks).Track);
        Assert.Equal(2, Assert.Single(topArtists).RawPlays);
        Assert.Equal(50m, behavior.RepeatRate);
        Assert.Equal(50m, behavior.SkipRate);
    }

    [Fact]
    public async Task Recording_analytics_merge_release_packaging_but_keep_live_version_separate()
    {
        await using var db = CreateContext();
        var january = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = new User { DisplayName = "Local User", CreatedAtUtc = january, UpdatedAtUtc = january };
        var artist = new Artist { Name = "Artist", NormalizedName = "ARTIST", CreatedAtUtc = january, UpdatedAtUtc = january };
        var standardAlbum = new Album { Name = "Album", NormalizedName = "ALBUM", PrimaryArtist = artist, CreatedAtUtc = january, UpdatedAtUtc = january };
        var deluxeAlbum = new Album { Name = "Album (Deluxe Edition)", NormalizedName = "ALBUM (DELUXE EDITION)", PrimaryArtist = artist, CreatedAtUtc = january, UpdatedAtUtc = january };
        var standard = Track("AAAAAAAAAAAAAAAAAAAAAA", "Song", standardAlbum, "USAAA2600001", 200_000, artist, january);
        var deluxe = Track("BBBBBBBBBBBBBBBBBBBBBB", "Song", deluxeAlbum, "USAAA2600001", 200_100, artist, january);
        var live = Track("CCCCCCCCCCCCCCCCCCCCCC", "Song - Live", deluxeAlbum, "USAAA2600001", 200_100, artist, january);
        db.ListeningEvents.AddRange(
            Event(user, standard, january, 'c'),
            Event(user, deluxe, january.AddMonths(1), 'd'),
            Event(user, live, january.AddMonths(1).AddDays(1), 'e'));
        await db.SaveChangesAsync();

        var analytics = new AnalyticsService(db);
        var overview = await analytics.GetOverviewAsync(null, null);
        var topTracks = await analytics.GetTopTracksAsync(null, null, 10);
        var topArtist = Assert.Single(await analytics.GetTopArtistsAsync(null, null, 10));
        var timeline = await analytics.GetTimelineAsync();
        var behavior = await analytics.GetBehaviorAsync(null, null);

        Assert.Equal(2, overview.UniqueTracks);
        Assert.Equal(2, topTracks.Count);
        Assert.Equal(2, topTracks.Single(x => x.Track == "Song").RawEvents);
        Assert.Equal(2, topArtist.UniqueTracks);
        Assert.Equal(1, timeline.Single(x => x.Period == "2026-01").NewTracks);
        Assert.Equal(1, timeline.Single(x => x.Period == "2026-02").NewTracks);
        Assert.Equal(33.3m, behavior.RepeatRate);
    }

    private static Track Track(string spotifyId, string name, Album album, string isrc, int durationMs, Artist artist, DateTime now)
    {
        var track = new Track
        {
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            SpotifyTrackId = spotifyId,
            SpotifyUri = $"spotify:track:{spotifyId}",
            Album = album,
            Isrc = isrc,
            DurationMs = durationMs,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        track.TrackArtists.Add(new TrackArtist { Track = track, Artist = artist, ArtistOrder = 0 });
        return track;
    }

    private static ListeningEvent Event(User user, Track track, DateTime playedAtUtc, char hash) => new()
    {
        User = user,
        Track = track,
        PlayedAtUtc = playedAtUtc,
        MsPlayed = 190_000,
        WasSkipped = false,
        Source = "test",
        ImportedAtUtc = playedAtUtc,
        ExternalEventHash = new string(hash, 64),
    };

    private static SpotifyTasteDbContext CreateContext() => new(new DbContextOptionsBuilder<SpotifyTasteDbContext>()
        .UseInMemoryDatabase($"spotify-analytics-{Guid.NewGuid():N}").Options);
}
