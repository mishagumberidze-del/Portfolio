using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.ReccoBeats;

namespace SpotifyTaste.Tests;

public sealed class ReccoBeatsTrackFeatureProviderTests
{
    [Fact]
    public async Task EnrichMissing_prioritizes_the_eligible_track_with_the_most_listening_events()
    {
        await using var db = CreateDb();
        var user = CreateUser();
        var low = CreateTrack("Low", "0000000000000000000001");
        var high = CreateTrack("High", "0000000000000000000002");
        var medium = CreateTrack("Medium", "0000000000000000000003");
        db.AddRange(user, low, high, medium);
        await db.SaveChangesAsync();
        AddEvents(db, user, low, 1, 'a');
        AddEvents(db, user, high, 4, 'b');
        AddEvents(db, user, medium, 2, 'c');
        await db.SaveChangesAsync();
        var client = new RecordingClient();

        var completed = await new ReccoBeatsTrackFeatureProvider(db, client).EnrichMissingAsync(1);

        Assert.Equal(1, completed);
        Assert.Equal([high.SpotifyTrackId!], client.RequestedSpotifyIds.ToArray());
        Assert.Equal(EnrichmentStatus.Completed, high.EnrichmentStatus);
        Assert.Equal(EnrichmentStatus.Pending, medium.EnrichmentStatus);
        Assert.Equal(EnrichmentStatus.Pending, low.EnrichmentStatus);
    }

    [Fact]
    public async Task EnrichMissing_includes_pending_and_retrylater_but_excludes_terminal_or_unidentifiable_tracks()
    {
        await using var db = CreateDb();
        var user = CreateUser();
        var pending = CreateTrack("Pending", "0000000000000000000011");
        var retryLater = CreateTrack("Retry", "0000000000000000000012", EnrichmentStatus.RetryLater);
        var completed = CreateTrack("Completed", "0000000000000000000013", EnrichmentStatus.Completed);
        var notFound = CreateTrack("Not found", "0000000000000000000014", EnrichmentStatus.NotFound);
        var withoutSpotifyId = CreateTrack("No ID", null);
        db.AddRange(user, pending, retryLater, completed, notFound, withoutSpotifyId);
        await db.SaveChangesAsync();
        AddEvents(db, user, pending, 1, 'd');
        AddEvents(db, user, retryLater, 1, 'e');
        AddEvents(db, user, completed, 5, 'f');
        AddEvents(db, user, notFound, 5, 'g');
        AddEvents(db, user, withoutSpotifyId, 5, 'h');
        await db.SaveChangesAsync();
        var client = new RecordingClient();

        var provider = new ReccoBeatsTrackFeatureProvider(db, client);
        var enriched = await provider.EnrichMissingAsync(10);
        var reenriched = await provider.EnrichMissingAsync(10);

        Assert.Equal(2, enriched);
        Assert.Equal(0, reenriched);
        Assert.Equal(
            new[] { pending.SpotifyTrackId!, retryLater.SpotifyTrackId! }.Order(),
            client.RequestedSpotifyIds.Order());
        Assert.Equal(2, await db.TrackFeatures.CountAsync());
        Assert.Equal(2, await db.TrackExternalIds.CountAsync());
    }

    [Fact]
    public async Task EnrichMissing_reuses_an_existing_provider_mapping_when_a_retry_succeeds()
    {
        await using var db = CreateDb();
        var track = CreateTrack("Retry", "0000000000000000000015", EnrichmentStatus.RetryLater);
        db.Add(track);
        db.TrackExternalIds.Add(new TrackExternalId
        {
            Track = track,
            Provider = "ReccoBeats",
            ExternalId = $"recco-{track.SpotifyTrackId}",
            CreatedAtUtc = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
        });
        await db.SaveChangesAsync();

        var completed = await new ReccoBeatsTrackFeatureProvider(db, new RecordingClient()).EnrichMissingAsync(1);

        Assert.Equal(1, completed);
        Assert.Equal(EnrichmentStatus.Completed, track.EnrichmentStatus);
        Assert.Single(await db.TrackExternalIds.ToListAsync());
        Assert.Single(await db.TrackFeatures.ToListAsync());
    }

    [Fact]
    public async Task EnrichMissing_uses_zero_listen_tracks_to_fill_the_requested_batch()
    {
        await using var db = CreateDb();
        var user = CreateUser();
        var listened = CreateTrack("Listened", "0000000000000000000021");
        var neverListened = CreateTrack("Never listened", "0000000000000000000022");
        db.AddRange(user, listened, neverListened);
        await db.SaveChangesAsync();
        AddEvents(db, user, listened, 2, 'i');
        await db.SaveChangesAsync();
        var client = new RecordingClient();

        var enriched = await new ReccoBeatsTrackFeatureProvider(db, client).EnrichMissingAsync(2);

        Assert.Equal(2, enriched);
        Assert.Equal(
            new[] { listened.SpotifyTrackId!, neverListened.SpotifyTrackId! }.Order(),
            client.RequestedSpotifyIds.Order());
    }

    [Fact]
    public async Task EnrichBatch_can_be_limited_to_tracks_that_exist_in_listening_history()
    {
        await using var db = CreateDb();
        var user = CreateUser();
        var listened = CreateTrack("Listened", "0000000000000000000031");
        var neverListened = CreateTrack("Never listened", "0000000000000000000032");
        db.AddRange(user, listened, neverListened);
        await db.SaveChangesAsync();
        AddEvents(db, user, listened, 1, 'j');
        await db.SaveChangesAsync();
        var client = new RecordingClient();

        var result = await new ReccoBeatsTrackFeatureProvider(db, client)
            .EnrichBatchAsync(10, historicalTracksOnly: true);

        Assert.Equal(1, result.Attempted);
        Assert.Equal(1, result.Completed);
        Assert.Equal(0, result.NotFound);
        Assert.Equal(0, result.RetryLater);
        Assert.Equal(0, result.RemainingEligible);
        Assert.Equal([listened.SpotifyTrackId!], client.RequestedSpotifyIds.ToArray());
        Assert.Equal(EnrichmentStatus.Pending, neverListened.EnrichmentStatus);
    }

    [Fact]
    public async Task EnrichBatch_reports_terminal_and_retryable_outcomes_separately()
    {
        await using var db = CreateDb();
        var user = CreateUser();
        var completed = CreateTrack("Completed", "0000000000000000000041");
        var missing = CreateTrack("Missing", "0000000000000000000042");
        var retry = CreateTrack("Retry", "0000000000000000000043");
        db.AddRange(user, completed, missing, retry);
        await db.SaveChangesAsync();
        AddEvents(db, user, completed, 3, 'k');
        AddEvents(db, user, missing, 2, 'l');
        AddEvents(db, user, retry, 1, 'm');
        await db.SaveChangesAsync();

        var result = await new ReccoBeatsTrackFeatureProvider(db, new MixedOutcomeClient())
            .EnrichBatchAsync(10, historicalTracksOnly: true);

        Assert.Equal(3, result.Attempted);
        Assert.Equal(1, result.Completed);
        Assert.Equal(1, result.NotFound);
        Assert.Equal(1, result.RetryLater);
        Assert.Equal(1, result.RemainingEligible);
        Assert.Equal(EnrichmentStatus.Completed, completed.EnrichmentStatus);
        Assert.Equal(EnrichmentStatus.NotFound, missing.EnrichmentStatus);
        Assert.Equal(EnrichmentStatus.RetryLater, retry.EnrichmentStatus);
    }

    private static SpotifyTasteDbContext CreateDb() => new(
        new DbContextOptionsBuilder<SpotifyTasteDbContext>()
            .UseInMemoryDatabase($"feature-provider-{Guid.NewGuid():N}")
            .Options);

    private static User CreateUser()
    {
        var now = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc);
        return new User { DisplayName = "Local User", CreatedAtUtc = now, UpdatedAtUtc = now };
    }

    private static Track CreateTrack(string name, string? spotifyTrackId, EnrichmentStatus status = EnrichmentStatus.Pending)
    {
        var now = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc);
        return new Track
        {
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            SpotifyTrackId = spotifyTrackId,
            SpotifyUri = spotifyTrackId is null ? null : $"spotify:track:{spotifyTrackId}",
            EnrichmentStatus = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static void AddEvents(SpotifyTasteDbContext db, User user, Track track, int count, char hashCharacter)
    {
        var playedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var index = 0; index < count; index++)
        {
            db.ListeningEvents.Add(new ListeningEvent
            {
                User = user,
                Track = track,
                PlayedAtUtc = playedAt.AddMinutes(index),
                MsPlayed = 60_000,
                Source = "test",
                ImportedAtUtc = playedAt,
                ExternalEventHash = new string(hashCharacter, 63) + index,
            });
        }
    }

    private sealed class RecordingClient : IReccoBeatsClient
    {
        public ConcurrentQueue<string> RequestedSpotifyIds { get; } = new();

        public Task<ExternalTrack?> GetBySpotifyIdAsync(string spotifyTrackId, CancellationToken cancellationToken = default)
        {
            RequestedSpotifyIds.Enqueue(spotifyTrackId);
            return Task.FromResult<ExternalTrack?>(new ExternalTrack(
                $"recco-{spotifyTrackId}",
                spotifyTrackId,
                $"Track {spotifyTrackId}",
                ["Artist"],
                null,
                180_000,
                null,
                $"https://open.spotify.com/track/{spotifyTrackId}",
                null,
                new TrackFeatureVector(120, .7, .6, .5, .1, 0, .04, .1, -6, 5, 1, 4)));
        }

        public Task<IReadOnlyList<ExternalTrack>> GetRecommendationsAsync(
            IReadOnlyList<string> positiveProviderIds,
            IReadOnlyList<string> negativeProviderIds,
            int limit,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExternalTrack>>([]);
    }

    private sealed class MixedOutcomeClient : IReccoBeatsClient
    {
        public Task<ExternalTrack?> GetBySpotifyIdAsync(string spotifyTrackId, CancellationToken cancellationToken = default) =>
            spotifyTrackId[^1] switch
            {
                '1' => Task.FromResult<ExternalTrack?>(new ExternalTrack(
                    $"recco-{spotifyTrackId}", spotifyTrackId, "Track", ["Artist"], null, 180_000, null,
                    $"https://open.spotify.com/track/{spotifyTrackId}", null,
                    new TrackFeatureVector(120, .7, .6, .5, .1, 0, .04, .1, -6, 5, 1, 4))),
                '2' => Task.FromResult<ExternalTrack?>(null),
                _ => Task.FromException<ExternalTrack?>(new HttpRequestException("429 Too Many Requests")),
            };

        public Task<IReadOnlyList<ExternalTrack>> GetRecommendationsAsync(
            IReadOnlyList<string> positiveProviderIds,
            IReadOnlyList<string> negativeProviderIds,
            int limit,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ExternalTrack>>([]);
    }
}
