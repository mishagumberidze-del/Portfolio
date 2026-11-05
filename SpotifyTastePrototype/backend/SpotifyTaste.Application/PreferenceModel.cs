namespace SpotifyTaste.Application;

public sealed record PreferenceModelOptions
{
    public int QualifiedPlayMs { get; init; } = 30_000;
    public double QualifiedDurationFraction { get; init; } = 0.5;
    public double TimeDecayHalfLifeDays { get; init; } = 365;
    public double SavedTrackBoost { get; init; } = 2.5;
    public double SkipPenalty { get; init; } = 1.25;
    public string ModelVersion { get; init; } = "preference-v1";
}

public sealed record PreferenceObservation(DateTime PlayedAtUtc, int MsPlayed, int? TrackDurationMs, bool? WasSkipped, bool IsSaved);
public sealed record BehavioralPreference(double PositiveWeight, double NegativeWeight, double RecencyWeight, double RepeatWeight, double CompletionWeight, double SkipPenalty, double SavedBoost, double Score);

public static class PreferenceModel
{
    public static BehavioralPreference Calculate(IReadOnlyList<PreferenceObservation> observations, DateTime nowUtc, PreferenceModelOptions? options = null)
    {
        options ??= new PreferenceModelOptions();
        if (observations.Count == 0) return new(0, 0, 0, 0, 0, 0, 0, 0);

        double positive = 0, negative = 0, recency = 0, completion = 0, skipPenalty = 0;
        foreach (var play in observations)
        {
            var ageDays = Math.Max(0, (nowUtc - play.PlayedAtUtc).TotalDays);
            var decay = Math.Pow(0.5, ageDays / options.TimeDecayHalfLifeDays);
            recency += decay;
            var completionRatio = play.TrackDurationMs is > 0 ? Math.Clamp(play.MsPlayed / (double)play.TrackDurationMs.Value, 0, 1.25) : Math.Clamp(play.MsPlayed / 180_000d, 0, 1);
            var qualified = play.MsPlayed >= options.QualifiedPlayMs || completionRatio >= options.QualifiedDurationFraction;
            if (qualified)
            {
                var contribution = decay * (0.65 + 0.85 * Math.Min(1, completionRatio));
                positive += contribution;
                completion += decay * Math.Min(1, completionRatio);
            }
            if (play.WasSkipped == true || play.MsPlayed < 10_000)
            {
                var penalty = decay * options.SkipPenalty * (play.MsPlayed < 5_000 ? 1.25 : 1);
                negative += penalty;
                skipPenalty += penalty;
            }
        }

        var repeatWeight = Math.Log2(1 + observations.Count) * Math.Min(1.5, recency / Math.Max(1, observations.Count) + 0.5);
        var savedBoost = observations.Any(x => x.IsSaved) ? options.SavedTrackBoost : 0;
        var raw = positive + repeatWeight + savedBoost - negative;
        var score = 10 / (1 + Math.Exp(-0.45 * (raw - 2.5)));
        return new(positive, negative, recency, repeatWeight, completion, skipPenalty, savedBoost, Math.Clamp(score, 0, 10));
    }

    public static double? WeightedSimilarity(TrackFeatureVector candidate, IEnumerable<(TrackFeatureVector Vector, double Weight)> examples)
    {
        var weighted = 0d;
        var total = 0d;
        foreach (var (vector, weight) in examples.Where(x => x.Weight > 0))
        {
            var similarity = FeatureSimilarity(candidate, vector);
            if (double.IsNaN(similarity)) continue;
            weighted += similarity * weight;
            total += weight;
        }
        return total == 0 ? null : Math.Clamp(weighted / total, 0, 1);
    }

    public static double FeatureSimilarity(TrackFeatureVector left, TrackFeatureVector right)
    {
        var pairs = new (double? A, double? B, double Scale)[]
        {
            (left.Tempo, right.Tempo, 80), (left.Energy, right.Energy, 1), (left.Danceability, right.Danceability, 1),
            (left.Valence, right.Valence, 1), (left.Acousticness, right.Acousticness, 1), (left.Instrumentalness, right.Instrumentalness, 1),
            (left.Speechiness, right.Speechiness, 1), (left.Liveness, right.Liveness, 1), (left.Loudness, right.Loudness, 30),
        };
        var distances = pairs.Where(x => x.A.HasValue && x.B.HasValue).Select(x => Math.Pow((x.A!.Value - x.B!.Value) / x.Scale, 2)).ToArray();
        var allDistances = distances.ToList();
        if (left.Key.HasValue && right.Key.HasValue)
        {
            var keyDistance = Math.Abs(left.Key.Value - right.Key.Value) % 12;
            allDistances.Add(Math.Pow(Math.Min(keyDistance, 12 - keyDistance) / 6d, 2));
        }
        if (left.Mode.HasValue && right.Mode.HasValue) allDistances.Add(left.Mode == right.Mode ? 0 : .25);
        if (left.TimeSignature.HasValue && right.TimeSignature.HasValue) allDistances.Add(Math.Pow((left.TimeSignature.Value - right.TimeSignature.Value) / 3d, 2));
        return allDistances.Count == 0 ? double.NaN : Math.Exp(-Math.Sqrt(allDistances.Average()) * 2.2);
    }
}
