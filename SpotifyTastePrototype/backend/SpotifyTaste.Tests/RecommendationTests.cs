using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.Recommendations;
using SpotifyTaste.Infrastructure.ReccoBeats;

namespace SpotifyTaste.Tests;

public sealed class RecommendationTests
{
    [Fact]
    public void Diversity_removes_duplicate_tracks_and_caps_artists_and_albums()
    {
        var selected = RecommendationDiversity.Select(new[]
        {
            new RankedCandidate("track-1", "artist-a", "album-a", 9.8),
            new RankedCandidate("track-1", "artist-a", "album-a", 9.7),
            new RankedCandidate("track-2", "artist-a", "album-b", 9.6),
            new RankedCandidate("track-3", "artist-b", "album-a", 9.5),
            new RankedCandidate("track-4", "artist-c", "album-c", 9.4),
        }, 10);

        Assert.Equal(new[] { "track-1", "track-4" }, selected.Select(x => x.Identity));
    }

    [Fact]
    public void Never_heard_eligibility_strictly_excludes_history_ids()
    {
        IReadOnlySet<string> history = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "known" };
        Assert.False(RecommendationEligibility.IsNeverHeard("KNOWN", history));
        Assert.True(RecommendationEligibility.IsNeverHeard("new", history));
    }

    [Fact]
    public void Not_saved_eligibility_strictly_excludes_saved_ids()
    {
        IReadOnlySet<string> saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "saved" };
        Assert.False(RecommendationEligibility.IsNotSaved("SAVED", saved));
        Assert.True(RecommendationEligibility.IsNotSaved("heard-unsaved", saved));
    }

    [Fact]
    public async Task Never_heard_excludes_deluxe_duplicate_but_keeps_true_live_version()
    {
        await using var db = CreateContext();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = new User { DisplayName = "Local User", CreatedAtUtc = now, UpdatedAtUtc = now };
        var artist = new Artist { Name = "Artist", NormalizedName = "ARTIST", CreatedAtUtc = now, UpdatedAtUtc = now };
        var heard = Track("AAAAAAAAAAAAAAAAAAAAAA", "Song", "Album", "USAAA2600001", artist, now);
        db.ListeningEvents.Add(new ListeningEvent
        {
            User = user,
            Track = heard,
            PlayedAtUtc = now,
            MsPlayed = 190_000,
            Source = "test",
            ImportedAtUtc = now,
            ExternalEventHash = new string('a', 64),
        });
        await db.SaveChangesAsync();

        var deluxe = External("BBBBBBBBBBBBBBBBBBBBBB", "Song", "Album (Deluxe Edition)", "USAAA2600001");
        var live = External("CCCCCCCCCCCCCCCCCCCCCC", "Song - Live", "Live at Somewhere", "USAAA2600001");
        var scorer = new FakeScorer();
        var service = new RecommendationService(
            db,
            new FakeCandidateProvider([deluxe, live]),
            scorer,
            NullLogger<RecommendationService>.Instance,
            Options.Create(new PreferenceModelOptions()));

        var result = await service.GenerateAsync(10);

        var recommendation = Assert.Single(result.NeverHeard);
        Assert.Equal("Song - Live", recommendation.Track);
        Assert.Equal("Never Heard", recommendation.HistoricalStatus);
        Assert.Equal(2, scorer.Calls);
    }

    [Fact]
    public async Task Candidate_pool_deduplicates_standard_and_deluxe_recording_before_scoring()
    {
        await using var db = CreateContext();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Users.Add(new User { DisplayName = "Local User", CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync();
        var standard = External("AAAAAAAAAAAAAAAAAAAAAA", "Song", "Album", "USAAA2600001");
        var deluxe = External("BBBBBBBBBBBBBBBBBBBBBB", "Song", "Album (Deluxe Edition)", "USAAA2600001");
        var live = External("CCCCCCCCCCCCCCCCCCCCCC", "Song - Live", "Live at Somewhere", "USAAA2600001");
        var scorer = new FakeScorer();
        var service = new RecommendationService(
            db,
            new FakeCandidateProvider([standard, deluxe, live]),
            scorer,
            NullLogger<RecommendationService>.Instance,
            Options.Create(new PreferenceModelOptions()));

        await service.GenerateAsync(10);

        Assert.Equal(2, scorer.Calls);
    }

    [Fact]
    public async Task Not_saved_excludes_deluxe_release_of_saved_recording()
    {
        await using var db = CreateContext();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = new User { DisplayName = "Local User", SpotifyLibraryDataAvailable = true, CreatedAtUtc = now, UpdatedAtUtc = now };
        var artist = new Artist { Name = "Artist", NormalizedName = "ARTIST", CreatedAtUtc = now, UpdatedAtUtc = now };
        var savedTrack = Track("AAAAAAAAAAAAAAAAAAAAAA", "Song", "Album", "USAAA2600001", artist, now);
        db.UserSavedTracks.Add(new UserSavedTrack { User = user, Track = savedTrack, Source = "test", ImportedAtUtc = now });
        await db.SaveChangesAsync();

        var deluxe = External("BBBBBBBBBBBBBBBBBBBBBB", "Song", "Album (Deluxe Edition)", "USAAA2600001");
        var live = External("CCCCCCCCCCCCCCCCCCCCCC", "Song - Live", "Live at Somewhere", "USAAA2600001");
        var service = new RecommendationService(
            db,
            new FakeCandidateProvider([deluxe, live]),
            new FakeScorer(),
            NullLogger<RecommendationService>.Instance,
            Options.Create(new PreferenceModelOptions()));

        var result = await service.GenerateAsync(10);

        Assert.True(result.SavedLibraryAvailable);
        var recommendation = Assert.Single(result.NotSaved);
        Assert.Equal("Song - Live", recommendation.Track);
        Assert.Equal("Never Heard", recommendation.HistoricalStatus);
    }

    [Fact]
    public async Task Failed_candidate_graph_does_not_pollute_later_candidates_or_insert_another_user()
    {
        await using var db = CreateContext(); db.Users.Add(new User { SpotifyLibraryDataAvailable = true }); await db.SaveChangesAsync();
        var candidates = new[] { External("AAAAAAAAAAAAAAAAAAAAAA", "Bad", "Album", "USAAA2600001"), External("BBBBBBBBBBBBBBBBBBBBBB", "Good", "Album", "USAAA2600002") };
        var service = new RecommendationService(db, new FakeCandidateProvider(candidates), new DirtyFailingScorer(db), NullLogger<RecommendationService>.Instance, Options.Create(new PreferenceModelOptions()));
        var result = await service.GenerateAsync(10);
        Assert.Single(result.NotSaved); Assert.Single(result.NeverHeard); Assert.Single(db.Users);
        Assert.False(await db.Tracks.AnyAsync(t => t.Name == "Uncommitted failed graph"));
        Assert.Equal(2, await db.RecommendationRuns.CountAsync());
    }

    [Fact]
    public async Task Provider_seeds_are_ranked_after_deduplication_not_by_database_row_order()
    {
        await using var db = CreateContext(); var user = new User();
        for (var i = 0; i < 45; i++)
        {
            var track = new Track { Name = $"Seed {i}" };
            db.TrackPreferences.Add(new TrackPreference { User = user, Track = track, ModelVersion = "preference-v1", BehavioralScore = i / 5m });
            db.TrackExternalIds.Add(new TrackExternalId { Track = track, Provider = "ReccoBeats", ExternalId = $"provider-{i:D2}" });
        }
        await db.SaveChangesAsync(); var client = new SeedCaptureClient();
        await new ReccoBeatsCandidateTrackProvider(db, client).GetCandidatesAsync(200);
        Assert.Equal("provider-44", client.Positive[0]); Assert.Equal("provider-00", client.Negative[0]);
        Assert.Equal(20, client.Positive.Count); Assert.Equal(20, client.Negative.Count);
        Assert.Empty(client.Positive.Intersect(client.Negative));
    }

    private sealed class SeedCaptureClient : IReccoBeatsClient
    {
        public IReadOnlyList<string> Positive { get; private set; } = []; public IReadOnlyList<string> Negative { get; private set; } = [];
        public Task<ExternalTrack?> GetBySpotifyIdAsync(string id, CancellationToken ct = default) => Task.FromResult<ExternalTrack?>(null);
        public Task<IReadOnlyList<ExternalTrack>> GetRecommendationsAsync(IReadOnlyList<string> positiveProviderIds, IReadOnlyList<string> negativeProviderIds, int limit, CancellationToken ct = default)
        { Positive = positiveProviderIds; Negative = negativeProviderIds; return Task.FromResult<IReadOnlyList<ExternalTrack>>([]); }
    }
    private sealed class DirtyFailingScorer(SpotifyTasteDbContext db) : ISongScoringService
    {
        public Task<ScoreResult> ScoreAsync(string url, CancellationToken ct = default)
        {
            var id = SpotifyTrackIdParser.Parse(url);
            if (id.StartsWith('A')) { db.Tracks.Add(new Track { Name = "Uncommitted failed graph" }); throw new InvalidOperationException("Synthetic candidate failure"); }
            return Task.FromResult(new ScoreResult(id, "Good", ["Artist"], 8, "High", new(8, 8, 8, 8, 5, 5, 0), ["Test"]));
        }
    }

    private static Track Track(string spotifyId, string name, string albumName, string isrc, Artist artist, DateTime now)
    {
        var album = new Album { Name = albumName, NormalizedName = albumName.ToUpperInvariant(), PrimaryArtist = artist, CreatedAtUtc = now, UpdatedAtUtc = now };
        var track = new Track
        {
            SpotifyTrackId = spotifyId,
            SpotifyUri = $"spotify:track:{spotifyId}",
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            Album = album,
            Isrc = isrc,
            DurationMs = 200_000,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        track.TrackArtists.Add(new TrackArtist { Track = track, Artist = artist, ArtistOrder = 0 });
        return track;
    }

    private static ExternalTrack External(string spotifyId, string name, string album, string isrc) => new(
        $"recco-{spotifyId}", spotifyId, name, ["Artist"], album, 200_000, isrc,
        $"https://open.spotify.com/track/{spotifyId}", null,
        new TrackFeatureVector(120, .7, .6, .5, .1, 0, .05, .1, -6, 1, 1, 4));

    private static SpotifyTasteDbContext CreateContext() => new(new DbContextOptionsBuilder<SpotifyTasteDbContext>()
        .UseInMemoryDatabase($"spotify-recommendations-{Guid.NewGuid():N}").Options);

    private sealed class FakeCandidateProvider(IReadOnlyList<ExternalTrack> candidates) : ICandidateTrackProvider
    {
        public string Name => "Test";
        public Task<IReadOnlyList<ExternalTrack>> GetCandidatesAsync(int desiredCount, CancellationToken cancellationToken = default) => Task.FromResult(candidates);
    }

    private sealed class FakeScorer : ISongScoringService
    {
        public int Calls { get; private set; }

        public Task<ScoreResult> ScoreAsync(string spotifyUrlOrUri, CancellationToken cancellationToken = default)
        {
            Calls++;
            var spotifyId = SpotifyTrackIdParser.Parse(spotifyUrlOrUri);
            var track = spotifyId.StartsWith('C') ? "Song - Live" : "Song";
            return Task.FromResult(new ScoreResult(
                spotifyId,
                track,
                ["Artist"],
                8,
                "High",
                new ScoreComponents(8, 8, 8, 8, 5, 5, 0),
                ["Test explanation"]));
        }
    }
}
