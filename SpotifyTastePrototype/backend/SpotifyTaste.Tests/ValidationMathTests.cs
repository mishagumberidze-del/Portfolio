using SpotifyTaste.Infrastructure.Preferences;
using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Application;
using SpotifyTaste.Domain;
using SpotifyTaste.Infrastructure.Persistence;

namespace SpotifyTaste.Tests;

public sealed class ValidationMathTests
{
    [Fact]
    public void Pairwise_concordance_credits_ties_and_keeps_empty_evidence_unknown()
    {
        Assert.Equal(.5, PreferenceModelService.PairwiseConcordance([5, 5], [5, 5]));
        Assert.Equal(.75, PreferenceModelService.PairwiseConcordance([7, 5], [5]));
        Assert.Equal(1, PreferenceModelService.PairwiseConcordance([7, 8], [1, 2]));
        Assert.Equal(0, PreferenceModelService.PairwiseConcordance([1, 2], [7, 8]));
        Assert.Null(PreferenceModelService.PairwiseConcordance([], [5]));
        Assert.Null(PreferenceModelService.PairwiseConcordance([5], []));
    }

    [Fact]
    public void Similarity_without_comparable_neighbors_is_unknown()
    {
        var feature = new TrackFeatureVector(120, .8, null, null, null, null, null, null, null);
        var missing = new TrackFeatureVector(null, null, null, null, null, null, null, null, null);
        Assert.Null(PreferenceModel.WeightedSimilarity(feature, []));
        Assert.Null(PreferenceModel.WeightedSimilarity(feature, [(missing, 1)]));
        Assert.Equal(1, PreferenceModel.WeightedSimilarity(feature, [(feature, 1)]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Benchmark_does_not_invent_separation_or_neighbors(bool equivalentReleases)
    {
        await using var db = new SpotifyTasteDbContext(new DbContextOptionsBuilder<SpotifyTasteDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = new User();
        var artist = new Artist { Name = "Artist" };
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 4; i++)
        {
            var track = new Track { Name = equivalentReleases ? "Same song" : $"Song {i}",
                SpotifyTrackId = new string((char)('a' + i), 22), DurationMs = 200000,
                Album = new Album { Name = "Album" }, TrackArtists = [new TrackArtist { Artist = artist }] };
            db.TrackFeatures.Add(new TrackFeature { Track = track, Provider = "ReccoBeats", Energy = .8m });
            // Same evidence cannot be split by ID; different evidence on equivalent
            // releases still cannot supply independent held-out neighbors.
            for (var play = 0; play < (equivalentReleases ? i + 1 : 1); play++)
                db.ListeningEvents.Add(new ListeningEvent { User = user, Track = track, PlayedAtUtc = at, MsPlayed = 190000 });
        }
        await db.SaveChangesAsync();
        var result = await new PreferenceModelService(db).RebuildAsync();
        Assert.Equal(4, result.TrackCount);
        Assert.Null(result.MeanPositiveScore);
        Assert.Null(result.MeanNegativeScore);
        Assert.Null(result.PairwiseAccuracy);
    }
}
