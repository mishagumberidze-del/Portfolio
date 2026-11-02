using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.Preferences;

namespace SpotifyTaste.Tests;

public sealed class SongScoringServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Score_is_bounded_deterministic_and_reuses_permanent_feature_cache()
    {
        await using var db = new SpotifyTasteDbContext(new DbContextOptionsBuilder<SpotifyTasteDbContext>()
            .UseInMemoryDatabase($"scoring-{Guid.NewGuid():N}").Options);
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var user = new User { DisplayName = "Local User", CreatedAtUtc = now, UpdatedAtUtc = now };
        var artist = new Artist { Name = "Known Artist", NormalizedName = "KNOWN ARTIST", CreatedAtUtc = now, UpdatedAtUtc = now };
        var track = new Track { Name = "Known", NormalizedName = "KNOWN", SpotifyTrackId = "0123456789012345678901", SpotifyUri = "spotify:track:0123456789012345678901", DurationMs = 200_000, CreatedAtUtc = now, UpdatedAtUtc = now };
        track.TrackArtists.Add(new TrackArtist { Track = track, Artist = artist, ArtistOrder = 0 });
        db.ListeningEvents.Add(new ListeningEvent { User = user, Track = track, PlayedAtUtc = now, MsPlayed = 190_000, WasSkipped = false, Source = "test", ImportedAtUtc = now, ExternalEventHash = new string('c', 64) });
        db.TrackFeatures.Add(new TrackFeature { Track = track, Provider = "ReccoBeats", Tempo = 120, Energy = .8m, Danceability = .6m, Valence = .5m, FetchedAtUtc = now });
        db.TrackPreferences.Add(new TrackPreference { User = user, Track = track, ModelVersion = "preference-v1", PositiveWeight = 5, BehavioralScore = 9, CalculatedAtUtc = now });
        await db.SaveChangesAsync();
        var external = new ExternalTrack("recco-new", "6fOqEAXCwboCO4BdzbZG1K", "Candidate", new[] { "Known Artist" }, null, 210_000, "TESTISRC", "https://open.spotify.com/track/6fOqEAXCwboCO4BdzbZG1K", null,
            new TrackFeatureVector(122, .79, .61, .52, .1, 0, .04, .1, -6, 5, 1, 4));
        var client = new CountingClient(external);
        var scorer = new SongScoringService(db, client, new UnusedModel());

        var first = await scorer.ScoreAsync(external.SpotifyUrl!);
        var second = await scorer.ScoreAsync(external.SpotifyUrl!);

        Assert.InRange(first.PredictedLikeScore, 0, 10);
        Assert.Equal(first.PredictedLikeScore, second.PredictedLikeScore);
        Assert.Equal(first.Components, second.Components);
        Assert.Equal(first.Explanations, second.Explanations);
        Assert.Equal(1, client.Calls);
        Assert.True(await db.TrackFeatures.AnyAsync(x => x.Track.SpotifyTrackId == external.SpotifyTrackId && x.Provider == "ReccoBeats"));
        Assert.True(await db.TrackExternalIds.AnyAsync(x => x.ExternalId == "recco-new"));
    }

    [Fact]
    public async Task Known_strong_favorite_scores_at_least_eight_point_five_despite_average_feature_match()
    {
        await using var fixture = await CalibrationFixture.CreateAsync();

        var result = await fixture.Scorer.ScoreExternalAsync(fixture.Favorite);

        Assert.InRange(result.Components.FeatureMatch, 4.5, 5.5);
        Assert.Equal(10, result.Components.HistoricalBehaviorMatch);
        Assert.True(result.PredictedLikeScore >= 8.5,
            $"Expected a high-evidence favorite to score at least 8.5, but it scored {result.PredictedLikeScore:0.0}.");
    }

    [Fact]
    public async Task Known_strong_favorite_beats_feature_identical_unseen_track_by_at_least_one_point_five()
    {
        await using var fixture = await CalibrationFixture.CreateAsync();

        var favorite = await fixture.Scorer.ScoreExternalAsync(fixture.Favorite);
        var unseen = await fixture.Scorer.ScoreExternalAsync(fixture.UnseenTwin);

        Assert.True(favorite.PredictedLikeScore - unseen.PredictedLikeScore >= 1.5,
            $"Expected observed favorite evidence to add at least 1.5 points; favorite={favorite.PredictedLikeScore:0.0}, unseen={unseen.PredictedLikeScore:0.0}.");
    }

    [Fact]
    public async Task Known_strong_favorite_beats_feature_identical_repeatedly_skipped_track_by_at_least_two_point_five()
    {
        await using var fixture = await CalibrationFixture.CreateAsync();

        var favorite = await fixture.Scorer.ScoreExternalAsync(fixture.Favorite);
        var skipped = await fixture.Scorer.ScoreExternalAsync(fixture.Skipped);

        Assert.True(favorite.PredictedLikeScore - skipped.PredictedLikeScore >= 2.5,
            $"Expected repeated positive and negative behavior to be separated by at least 2.5 points; favorite={favorite.PredictedLikeScore:0.0}, skipped={skipped.PredictedLikeScore:0.0}.");
    }

    [Fact]
    public async Task One_good_play_does_not_receive_the_full_strong_history_boost()
    {
        await using var fixture = await CalibrationFixture.CreateAsync();

        var favorite = await fixture.Scorer.ScoreExternalAsync(fixture.Favorite);
        var singlePlay = await fixture.Scorer.ScoreExternalAsync(fixture.SinglePlay);
        var unseen = await fixture.Scorer.ScoreExternalAsync(fixture.UnseenTwin);

        Assert.True(singlePlay.Components.HistoricalBehaviorMatch >= 9.5);
        // A single observation may influence the result, but must contribute no
        // more than a 25% history blend: (9.8 - neutral 5.0) * .25 = 1.2.
        Assert.InRange(Math.Abs(singlePlay.PredictedLikeScore - unseen.PredictedLikeScore), 0, 1.2);
        Assert.True(favorite.PredictedLikeScore - singlePlay.PredictedLikeScore >= 1.5,
            $"One observation should not carry the confidence of repeated listening; favorite={favorite.PredictedLikeScore:0.0}, one-play={singlePlay.PredictedLikeScore:0.0}.");
    }

    [Fact]
    public async Task Equivalent_spotify_ids_sharing_normalized_isrc_aggregate_observed_behavior()
    {
        await using var fixture = await CalibrationFixture.CreateAsync();
        const string isrc = "USAAA2600005";
        var first = fixture.AddKnownTrack("HHHHHHHHHHHHHHHHHHHHHH", isrc, 20, 10, skipped: false, name: "Equivalent Song");
        fixture.AddKnownTrack("IIIIIIIIIIIIIIIIIIIIII", " usaaa2600005 ", 10, 8, skipped: false, name: "Equivalent Song");
        await fixture.Db.SaveChangesAsync();
        var equivalentId = CalibrationFixture.External(
            "JJJJJJJJJJJJJJJJJJJJJJ", "US-AAA-26-00005", energy: .5, name: "Equivalent Song");

        var equivalent = await fixture.Scorer.ScoreExternalAsync(equivalentId);
        var exact = await fixture.Scorer.ScoreExternalAsync(first);

        // (20 * 10 + 10 * 8) / 30 = 9.333..., rounded for display.
        Assert.InRange(equivalent.Components.HistoricalBehaviorMatch, 9.2, 9.4);
        Assert.Equal(equivalent.Components.HistoricalBehaviorMatch, exact.Components.HistoricalBehaviorMatch);
        Assert.InRange(Math.Abs(equivalent.PredictedLikeScore - exact.PredictedLikeScore), 0, 0.3);
    }

    [Fact]
    public async Task Missing_library_is_neutral_and_does_not_average_a_strong_available_signal_down()
    {
        await using var fixture = await CalibrationFixture.CreateStrongAvailableSignalsAsync();

        var result = await fixture.Scorer.ScoreExternalAsync(fixture.UnseenTwin);

        Assert.Equal(5, result.Components.SavedTrackMatch);
        Assert.Contains(result.Explanations, x => x.Contains("signal is excluded", StringComparison.OrdinalIgnoreCase));
        Assert.True(result.PredictedLikeScore >= 9,
            $"A display-only neutral saved component must not depress available strong evidence; score={result.PredictedLikeScore:0.0}.");
    }

    [Fact]
    public async Task Saved_neighborhood_includes_tracks_after_first_500_without_inventing_history()
    {
        await using var fixture = await CalibrationFixture.CreateAsync();
        var user = await fixture.Db.Users.SingleAsync(); user.SpotifyLibraryDataAvailable = true;
        for (var i = 0; i < 540; i++)
        {
            var track = new Track { SpotifyTrackId = i.ToString().PadLeft(22, '0'), Name = $"Distinct saved song {i}", DurationMs = 200000 };
            fixture.Db.UserSavedTracks.Add(new UserSavedTrack { User = user, Track = track });
            fixture.Db.TrackFeatures.Add(new TrackFeature { Track = track, Provider = "ReccoBeats", Energy = i < 500 ? 0 : 1 });
        }
        await fixture.Db.SaveChangesAsync(); var historyBefore = await fixture.Db.ListeningEvents.CountAsync();
        var result = await fixture.Scorer.ScoreExternalAsync(CalibrationFixture.External("ZZZZZZZZZZZZZZZZZZZZZZ", "USAAA2699999", 1, "Unheard candidate"));
        Assert.Equal(10, result.Components.SavedTrackMatch);
        Assert.False(result.KnownRecording); Assert.Equal(0, result.HistoricalEvents);
        Assert.Equal(historyBefore, await fixture.Db.ListeningEvents.CountAsync());
    }

    [Fact]
    public async Task Saved_only_track_is_recognized_without_being_called_previously_heard()
    {
        await using var fixture = await CalibrationFixture.CreateAsync();
        var user = await fixture.Db.Users.SingleAsync(); user.SpotifyLibraryDataAvailable = true;
        var saved = new Track { SpotifyTrackId = "ZZZZZZZZZZZZZZZZZZZZZZ", Name = "Saved only", DurationMs = 200000 };
        fixture.Db.UserSavedTracks.Add(new UserSavedTrack { User = user, Track = saved }); await fixture.Db.SaveChangesAsync();
        var result = await fixture.Scorer.ScoreExternalAsync(CalibrationFixture.External(saved.SpotifyTrackId, "USAAA2699999", 1, saved.Name));
        Assert.Equal(10, result.Components.SavedTrackMatch); Assert.False(result.KnownRecording); Assert.Equal(0, result.HistoricalEvents);
    }

    [Fact]
    public async Task Saved_boost_contributes_to_positive_feature_neighborhood()
    {
        await using var fixture = await CalibrationFixture.CreateAsync();
        var user = await fixture.Db.Users.SingleAsync(); user.SpotifyLibraryDataAvailable = true;
        foreach (var row in await fixture.Db.TrackPreferences.ToListAsync())
        { row.PositiveWeight = 0; row.NegativeWeight = 0; row.SavedBoost = 0; }
        var rowWithLike = await fixture.Db.TrackPreferences.Include(p => p.Track).SingleAsync(p => p.Track.SpotifyTrackId == "EEEEEEEEEEEEEEEEEEEEEE");
        rowWithLike.SavedBoost = 2.5m;
        await fixture.Db.SaveChangesAsync();
        var result = await fixture.Scorer.ScoreExternalAsync(CalibrationFixture.External("ZZZZZZZZZZZZZZZZZZZZZZ", "USAAA2699999", .185, "Unheard candidate"));
        Assert.Equal(10, result.Components.LifetimeTasteMatch);
    }

    [Fact]
    public async Task Repeated_existing_artist_credit_is_stored_once()
    {
        await using var fixture = await CalibrationFixture.CreateStrongAvailableSignalsAsync();
        var candidate = CalibrationFixture.External("ZZZZZZZZZZZZZZZZZZZZZZ", "USAAA2699999", .5, "Repeated credits") with { Artists = ["Known Artist", "Known Artist"] };
        await fixture.Scorer.ScoreExternalAsync(candidate);
        var persisted = await fixture.Db.Tracks.Include(t => t.TrackArtists).SingleAsync(t => t.SpotifyTrackId == candidate.SpotifyTrackId);
        Assert.Single(persisted.TrackArtists); Assert.Equal(0, persisted.TrackArtists.Single().ArtistOrder);
    }

    private sealed class CountingClient(ExternalTrack track) : IReccoBeatsClient
    {
        public int Calls { get; private set; }
        public Task<ExternalTrack?> GetBySpotifyIdAsync(string spotifyTrackId, CancellationToken cancellationToken = default) { Calls++; return Task.FromResult<ExternalTrack?>(track); }
        public Task<IReadOnlyList<ExternalTrack>> GetRecommendationsAsync(IReadOnlyList<string> positiveProviderIds, IReadOnlyList<string> negativeProviderIds, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExternalTrack>>([]);
    }

    private sealed class UnusedModel : IPreferenceModelService
    {
        public Task<PreferenceValidationDto> RebuildAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Model should already exist.");
    }

    private sealed class CalibrationFixture : IAsyncDisposable
    {
        private const string Provider = "ReccoBeats";
        private readonly User user;

        private CalibrationFixture(SpotifyTasteDbContext db, User user)
        {
            Db = db;
            this.user = user;
            Scorer = new SongScoringService(db, new NullClient(), new UnusedModel());
        }

        public SpotifyTasteDbContext Db { get; }
        public SongScoringService Scorer { get; }
        public ExternalTrack Favorite { get; private set; } = null!;
        public ExternalTrack Skipped { get; private set; } = null!;
        public ExternalTrack SinglePlay { get; private set; } = null!;
        public ExternalTrack UnseenTwin { get; private set; } = null!;

        public static async Task<CalibrationFixture> CreateAsync()
        {
            var db = CreateContext();
            var user = new User { DisplayName = "Local User", CreatedAtUtc = Now, UpdatedAtUtc = Now, SpotifyLibraryDataAvailable = false };
            db.Users.Add(user);
            var fixture = new CalibrationFixture(db, user);

            // These large, equally distant positive and negative neighbors make the
            // candidate's feature evidence deliberately average (about 5/10).
            fixture.AddPreferenceTrack("EEEEEEEEEEEEEEEEEEEEEE", "USAAA2600010", .185, 1, 9, positiveWeight: 1000, negativeWeight: 0);
            fixture.AddPreferenceTrack("FFFFFFFFFFFFFFFFFFFFFF", "USAAA2600011", .815, 1, 1, positiveWeight: 0, negativeWeight: 1000, skipped: true);

            fixture.Favorite = fixture.AddKnownTrack("AAAAAAAAAAAAAAAAAAAAAA", "USAAA2600001", 50, 10, skipped: false);
            fixture.Skipped = fixture.AddKnownTrack("BBBBBBBBBBBBBBBBBBBBBB", "USAAA2600002", 50, 0, skipped: true);
            fixture.SinglePlay = fixture.AddKnownTrack("CCCCCCCCCCCCCCCCCCCCCC", "USAAA2600003", 1, 9.8, skipped: false);
            fixture.UnseenTwin = External("DDDDDDDDDDDDDDDDDDDDDD", "USAAA2600004", energy: .5);
            fixture.AddClockAnchor();
            await db.SaveChangesAsync();
            return fixture;
        }

        public static async Task<CalibrationFixture> CreateStrongAvailableSignalsAsync()
        {
            var db = CreateContext();
            var user = new User { DisplayName = "Local User", CreatedAtUtc = Now, UpdatedAtUtc = Now, SpotifyLibraryDataAvailable = false };
            db.Users.Add(user);
            var fixture = new CalibrationFixture(db, user);
            var knownArtist = new Artist { Name = "Known Artist", NormalizedName = "KNOWN ARTIST", CreatedAtUtc = Now, UpdatedAtUtc = Now };
            var neighbor = fixture.AddPreferenceTrack("EEEEEEEEEEEEEEEEEEEEEE", "USAAA2600010", .5, 1, 9, positiveWeight: 1000, negativeWeight: 0);
            neighbor.TrackArtists.Add(new TrackArtist { Track = neighbor, Artist = knownArtist, ArtistOrder = 0 });
            fixture.UnseenTwin = External("DDDDDDDDDDDDDDDDDDDDDD", "USAAA2600004", energy: .5, artist: knownArtist.Name);
            fixture.AddClockAnchor();
            await db.SaveChangesAsync();
            return fixture;
        }

        public ExternalTrack AddKnownTrack(string spotifyId, string isrc, int eventCount, double behavioralScore, bool skipped, string? name = null)
        {
            var track = AddPreferenceTrack(
                spotifyId,
                isrc,
                .5,
                eventCount,
                behavioralScore,
                positiveWeight: skipped ? 0 : eventCount,
                negativeWeight: skipped ? eventCount : 0,
                skipped,
                name);
            return External(spotifyId, isrc, energy: .5, name: track.Name);
        }

        private Track AddPreferenceTrack(
            string spotifyId,
            string isrc,
            double energy,
            int eventCount,
            double behavioralScore,
            double positiveWeight,
            double negativeWeight,
            bool skipped = false,
            string? name = null)
        {
            var track = new Track
            {
                Name = name ?? $"Synthetic {spotifyId[0]}",
                NormalizedName = (name ?? $"Synthetic {spotifyId[0]}").ToUpperInvariant(),
                SpotifyTrackId = spotifyId,
                SpotifyUri = $"spotify:track:{spotifyId}",
                SpotifyUrl = $"https://open.spotify.com/track/{spotifyId}",
                Isrc = isrc,
                DurationMs = 200_000,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now,
            };
            Db.Tracks.Add(track);
            Db.TrackFeatures.Add(new TrackFeature { Track = track, Provider = Provider, Energy = (decimal)energy, FetchedAtUtc = Now });
            Db.TrackExternalIds.Add(new TrackExternalId { Track = track, Provider = Provider, ExternalId = $"recco-{spotifyId}", CreatedAtUtc = Now });
            Db.TrackPreferences.Add(new TrackPreference
            {
                User = user,
                Track = track,
                ModelVersion = "preference-v1",
                PositiveWeight = (decimal)positiveWeight,
                NegativeWeight = (decimal)negativeWeight,
                BehavioralScore = (decimal)behavioralScore,
                CalculatedAtUtc = Now,
            });
            for (var index = 0; index < eventCount; index++)
            {
                Db.ListeningEvents.Add(new ListeningEvent
                {
                    User = user,
                    Track = track,
                    PlayedAtUtc = Now.AddDays(-200 - index),
                    MsPlayed = skipped ? 3_000 : 190_000,
                    WasSkipped = skipped,
                    Source = "test",
                    ImportedAtUtc = Now,
                    ExternalEventHash = Guid.NewGuid().ToString("N").PadRight(64, '0'),
                });
            }
            return track;
        }

        private void AddClockAnchor()
        {
            var anchor = new Track
            {
                Name = "Clock Anchor",
                NormalizedName = "CLOCK ANCHOR",
                SpotifyTrackId = "GGGGGGGGGGGGGGGGGGGGGG",
                SpotifyUri = "spotify:track:GGGGGGGGGGGGGGGGGGGGGG",
                DurationMs = 200_000,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now,
            };
            Db.ListeningEvents.Add(new ListeningEvent
            {
                User = user,
                Track = anchor,
                PlayedAtUtc = Now,
                MsPlayed = 30_000,
                Source = "test",
                ImportedAtUtc = Now,
                ExternalEventHash = Guid.NewGuid().ToString("N").PadRight(64, '0'),
            });
        }

        public static ExternalTrack External(string spotifyId, string isrc, double energy, string name = "Synthetic Candidate", string? artist = null) => new(
            $"recco-{spotifyId}",
            spotifyId,
            name,
            artist is null ? [] : [artist],
            null,
            200_000,
            isrc,
            $"https://open.spotify.com/track/{spotifyId}",
            null,
            new TrackFeatureVector(null, energy, null, null, null, null, null, null, null));

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private static SpotifyTasteDbContext CreateContext() => new(new DbContextOptionsBuilder<SpotifyTasteDbContext>()
            .UseInMemoryDatabase($"scoring-calibration-{Guid.NewGuid():N}").Options);
    }

    private sealed class NullClient : IReccoBeatsClient
    {
        public Task<ExternalTrack?> GetBySpotifyIdAsync(string spotifyTrackId, CancellationToken cancellationToken = default) => Task.FromResult<ExternalTrack?>(null);
        public Task<IReadOnlyList<ExternalTrack>> GetRecommendationsAsync(IReadOnlyList<string> positiveProviderIds, IReadOnlyList<string> negativeProviderIds, int limit, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExternalTrack>>([]);
    }
}
