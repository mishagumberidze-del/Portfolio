using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;
using SpotifyTaste.Infrastructure.Recommendations;

namespace SpotifyTaste.Tests;

public sealed class FeedbackTests
{
    private static SpotifyTasteDbContext Db() => new(new DbContextOptionsBuilder<SpotifyTasteDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static RecommendationCandidate Candidate() => new() { Track = new Track { Name = "Song", SpotifyTrackId = new string('A', 22), DurationMs = 200000,
        TrackArtists = [new TrackArtist { Artist = new Artist { Name = "Artist" } }] }, RecommendationRun = new RecommendationRun { User = new User(), ListType = RecommendationListType.NeverHeard }, Rank = 1, NoveltyScore = 10, Confidence = "High" };

    [Fact]
    public async Task Feedback_appends_history_without_touching_Spotify_facts_or_saved_state()
    {
        await using var db = Db(); var c = Candidate(); db.RecommendationCandidates.Add(c); await db.SaveChangesAsync();
        var s = new FeedbackService(db, new EmptyRecommendations());
        var first = await s.AddAsync(new(c.Id, "Love")); var next = await s.AddAsync(new(c.Id, "NotForMe"));
        Assert.NotEqual(first.Id, next.Id); Assert.True(next.CreatedAtUtc >= first.CreatedAtUtc);
        Assert.Equal(2, await db.RecommendationFeedback.CountAsync()); Assert.Empty(db.ListeningEvents); Assert.Empty(db.UserSavedTracks);
        Assert.Equal("NotForMe", (await s.LatestAsync()).NeverHeard[0].Feedback);
    }
    [Theory]
    [InlineData("Bogus", null)] [InlineData("Rating", 0)] [InlineData("Rating", 6)] [InlineData("Love", 5)]
    public async Task Invalid_feedback_is_rejected(string kind, int? rating)
    {
        await using var db = Db(); var service = new FeedbackService(db, new EmptyRecommendations());
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(new(1, kind, (byte?)rating)));
    }
    [Fact]
    public async Task Rating_cannot_be_attached_to_a_different_evaluation_candidate()
    {
        await using var db = Db(); var c = Candidate(); db.RecommendationCandidates.Add(c); await db.SaveChangesAsync();
        var service = new FeedbackService(db, new EmptyRecommendations());
        await Assert.ThrowsAsync<ArgumentException>(() => service.AddAsync(new(c.Id, "Rating", 4, 999)));
        Assert.Empty(db.RecommendationFeedback);
    }
    [Fact]
    public async Task Evaluation_resumes_and_latest_rating_wins_without_deleting_old_labels()
    {
        await using var db = Db(); var c = Candidate(); db.RecommendationCandidates.Add(c); await db.SaveChangesAsync();
        var service = new FeedbackService(db, new EmptyRecommendations()); var session = await service.StartAsync(50);
        Assert.Equal(session.Id, (await service.StartAsync(100)).Id);
        var item = new EvaluationItem { EvaluationSessionId = session.Id, RecommendationCandidateId = c.Id, Position = 1 };
        db.EvaluationItems.Add(item); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => service.NextAsync());
        await service.AddAsync(new(c.Id, "Rating", 2, item.Id)); await service.AddAsync(new(c.Id, "Rating", 5, item.Id));
        var result = (await service.GetAsync())!; Assert.Equal(1, result.Metrics.Rated); Assert.Equal(5d, result.Metrics.AverageRating);
        Assert.Equal(2, db.RecommendationFeedback.Count());
        await service.AddAsync(new(c.Id, "AlreadyKnow", EvaluationItemId: item.Id));
        result = (await service.GetAsync())!; Assert.Equal(0, result.Metrics.Rated); Assert.Equal(1, result.Metrics.AlreadyKnown);
    }
    [Fact]
    public async Task Shown_and_rejected_recordings_are_suppressed_across_deluxe_releases()
    {
        await using var db = Db(); var c = Candidate(); db.RecommendationCandidates.Add(c); await db.SaveChangesAsync();
        db.RecommendationFeedback.Add(new() { RecommendationCandidateId = c.Id, Kind = "NotForMe", CreatedAtUtc = DateTime.UtcNow }); await db.SaveChangesAsync();
        var provider = new Provider([new("test", new string('B', 22), "Song", ["Artist"], "Deluxe", 200000, null, null, null, null)]);
        var service = new RecommendationService(db, provider, new Scorer(), NullLogger<RecommendationService>.Instance, Options.Create(new PreferenceModelOptions()));
        Assert.Empty((await service.GenerateAsync(10)).NeverHeard);
        Assert.Empty(db.ListeningEvents);
    }
    [Fact]
    public void Evaluation_denominators_exclude_known_and_unrated_tracks_and_keep_neutral_separate()
    {
        var rows = Enumerable.Range(1, 10).Select(i => new EvaluationRating(i, "HighConfidence", (byte)(i <= 6 ? 5 : i == 7 ? 3 : 1), false)).ToList();
        rows.Add(new(11, "Wildcard", null, false)); rows.Add(new(12, "Discovery", 1, true));
        var m = EvaluationMath.Calculate(rows);
        Assert.Equal(.6, m.PrecisionAt10); Assert.Equal(.6, m.AcceptanceRate); Assert.Equal(.3, m.RejectionRate);
        Assert.Equal(10, m.Rated); Assert.Equal(1, m.AlreadyKnown); Assert.Equal(6, m.Bands[0].Accepted);
        Assert.Null(m.Bands[2].AcceptanceRate);
    }
    [Fact]
    public async Task Latest_not_saved_list_rechecks_new_library_entries_across_releases()
    {
        await using var db = Db(); var c = Candidate(); c.RecommendationRun.ListType = RecommendationListType.NotSaved;
        c.RecommendationRun.User.SpotifyLibraryDataAvailable = true;
        db.RecommendationCandidates.Add(c); await db.SaveChangesAsync();
        var service = new FeedbackService(db, new EmptyRecommendations());
        Assert.Single((await service.LatestAsync()).NotSaved);
        var alias = new Track { Name = "Song", SpotifyTrackId = new string('B', 22), DurationMs = 200001,
            TrackArtists = [new TrackArtist { Artist = c.Track.TrackArtists.First().Artist }] };
        db.UserSavedTracks.Add(new() { Track = alias, User = c.RecommendationRun.User }); await db.SaveChangesAsync();
        Assert.Empty((await service.LatestAsync()).NotSaved);
    }
    [Fact]
    public async Task New_history_removes_equivalent_recording_from_latest_list_and_evaluation_metrics()
    {
        await using var db = Db(); var c = Candidate(); db.RecommendationCandidates.Add(c); await db.SaveChangesAsync();
        var service = new FeedbackService(db, new EmptyRecommendations()); var session = await service.StartAsync(50);
        var item = new EvaluationItem { EvaluationSessionId = session.Id, RecommendationCandidateId = c.Id, Position = 1 };
        db.EvaluationItems.Add(item); await db.SaveChangesAsync(); await service.AddAsync(new(c.Id, "Rating", 5, item.Id));
        Assert.Equal(1, (await service.GetAsync())!.Metrics.Rated);
        var alias = new Track { Name = "Song", SpotifyTrackId = new string('B', 22), DurationMs = 200000,
            TrackArtists = [new TrackArtist { Artist = c.Track.TrackArtists.First().Artist }] };
        db.ListeningEvents.Add(new() { Track = alias, User = c.RecommendationRun.User, MsPlayed = 200000, PlayedAtUtc = DateTime.UtcNow, ExternalEventHash = new string('Z', 64) });
        await db.SaveChangesAsync();
        Assert.Empty((await service.LatestAsync()).NeverHeard);
        Assert.Equal(0, (await service.GetAsync())!.Metrics.Rated);
        Assert.Equal(1, (await service.GetAsync())!.Metrics.AlreadyKnown);
        Assert.Single(db.RecommendationFeedback);
    }
    [Fact]
    public async Task Evaluation_batches_are_persisted_with_stable_positions_and_resume_after_rating()
    {
        await using var db = Db(); var service = new FeedbackService(db, new BatchRecommendations(db));
        await service.StartAsync(50); var first = await service.NextAsync();
        Assert.Equal(10, first.Items.Count); Assert.Equal(Enumerable.Range(1, 10), first.Items.Select(i => i.Position));
        await Assert.ThrowsAsync<ArgumentException>(() => service.NextAsync());
        foreach (var i in first.Items) await service.AddAsync(new(i.Recommendation.CandidateId, "Rating", 4, i.Id));
        var second = await service.NextAsync();
        Assert.Equal(20, second.Items.Count); Assert.Equal(20, db.EvaluationItems.Count());
        Assert.Equal(10, second.Metrics.Rated); Assert.Equal(1d, second.Metrics.PrecisionAt10);
        Assert.Equal(first.Items.Select(i => i.Id), second.Items.Take(10).Select(i => i.Id));
    }
    [Fact]
    public void Precision_requires_all_first_ten_eligible_positions_not_any_ten_ratings()
    {
        var rows = Enumerable.Range(1, 11).Select(i => new EvaluationRating(i, "Discovery", i == 1 ? null : (byte)5, false)).ToArray();
        Assert.Null(EvaluationMath.Calculate(rows).PrecisionAt10);
        rows[0] = rows[0] with { AlreadyKnown = true };
        Assert.Equal(1d, EvaluationMath.Calculate(rows).PrecisionAt10);
        Assert.Null(EvaluationMath.Calculate([]).AverageRating);
    }
    private sealed class EmptyRecommendations : IRecommendationService { public Task<DiscoverDto> GenerateAsync(int countPerList, CancellationToken cancellationToken = default) => Task.FromResult(new DiscoverDto(false, "Unavailable", [], [])); }
    private sealed class BatchRecommendations(SpotifyTasteDbContext db) : IRecommendationService
    {
        public async Task<DiscoverDto> GenerateAsync(int countPerList, CancellationToken cancellationToken = default)
        {
            var start = db.RecommendationCandidates.Count(); var candidates = Enumerable.Range(1, countPerList).Select(i =>
            {
                var c = Candidate(); c.Track.Name = $"Song {start + i}"; c.Track.SpotifyTrackId = (start + i).ToString().PadLeft(22, '0'); return c;
            }).ToArray();
            db.RecommendationCandidates.AddRange(candidates); await db.SaveChangesAsync(cancellationToken);
            return new(false, "Unavailable", [], candidates.Select(c => FeedbackService.ToDto(c)).ToArray());
        }
    }
    private sealed class Provider(IReadOnlyList<ExternalTrack> tracks) : ICandidateTrackProvider { public string Name => "Test"; public Task<IReadOnlyList<ExternalTrack>> GetCandidatesAsync(int desiredCount, CancellationToken cancellationToken = default) => Task.FromResult(tracks); }
    private sealed class Scorer : ISongScoringService { public Task<ScoreResult> ScoreAsync(string spotifyUrlOrUri, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Suppressed candidates must not be scored."); }
}
