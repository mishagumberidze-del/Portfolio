using SpotifyTaste.Application;

namespace SpotifyTaste.Tests;

public sealed class PreferenceModelTests
{
    private static readonly DateTime Now = new(2026, 6, 14, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Repeated_completed_track_scores_above_repeated_skipped_track()
    {
        var positive = Enumerable.Range(0, 5).Select(i => new PreferenceObservation(Now.AddDays(-i), 190_000, 200_000, false, false)).ToList();
        var negative = Enumerable.Range(0, 5).Select(i => new PreferenceObservation(Now.AddDays(-i), 3_000, 200_000, true, false)).ToList();
        Assert.True(PreferenceModel.Calculate(positive, Now).Score > PreferenceModel.Calculate(negative, Now).Score);
    }

    [Fact]
    public void Time_decay_makes_recent_behavior_more_influential()
    {
        var recent = new[] { new PreferenceObservation(Now.AddDays(-1), 180_000, 200_000, false, false) };
        var old = new[] { new PreferenceObservation(Now.AddYears(-5), 180_000, 200_000, false, false) };
        Assert.True(PreferenceModel.Calculate(recent, Now).Score > PreferenceModel.Calculate(old, Now).Score);
    }

    [Fact]
    public void Skip_penalty_reduces_otherwise_identical_behavior()
    {
        var completed = new[] { new PreferenceObservation(Now, 180_000, 200_000, false, false) };
        var skipped = new[] { new PreferenceObservation(Now, 180_000, 200_000, true, false) };
        Assert.True(PreferenceModel.Calculate(completed, Now).Score > PreferenceModel.Calculate(skipped, Now).Score);
    }

    [Fact]
    public void Real_saved_state_provides_a_strong_boost()
    {
        var ordinary = new[] { new PreferenceObservation(Now, 45_000, 200_000, false, false) };
        var saved = new[] { ordinary[0] with { IsSaved = true } };
        Assert.True(PreferenceModel.Calculate(saved, Now).Score > PreferenceModel.Calculate(ordinary, Now).Score);
    }

    [Fact]
    public void Feature_similarity_is_bounded_and_deterministic()
    {
        var vector = new TrackFeatureVector(120, .8, .6, .5, .1, 0, .05, .1, -6);
        var first = PreferenceModel.FeatureSimilarity(vector, vector);
        var second = PreferenceModel.FeatureSimilarity(vector, vector);
        Assert.Equal(first, second);
        Assert.InRange(first, 0, 1);
    }
}
