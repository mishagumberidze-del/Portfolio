namespace SpotifyTaste.Application;

public sealed record FeedbackRequest(long CandidateId, string Kind, byte? Rating = null, long? EvaluationItemId = null);
public sealed record FeedbackDto(long Id, string Kind, byte? Rating, DateTime CreatedAtUtc);
public sealed record EvaluationRating(int Position, string Band, byte? Rating, bool AlreadyKnown);
public sealed record BandMetric(string Band, int Rated, int Accepted, double? AcceptanceRate);
public sealed record EvaluationMetrics(int Presented, int Rated, int AlreadyKnown, int Accepted, int Rejected,
    double? PrecisionAt10, double? AverageRating, double? AcceptanceRate, double? RejectionRate, IReadOnlyList<BandMetric> Bands);
public sealed record EvaluationEntry(long Id, int Position, RecommendationDto Recommendation, byte? Rating, bool AlreadyKnown);
public sealed record EvaluationDto(long Id, int TargetCount, DateTime CreatedAtUtc, IReadOnlyList<EvaluationEntry> Items, EvaluationMetrics Metrics);

public static class EvaluationMath
{
    // Only explicit evaluation ratings count. 3 is neutral. Known songs are not predictions of novelty.
    public static EvaluationMetrics Calculate(IReadOnlyList<EvaluationRating> rows)
    {
        var eligible = rows.Where(x => !x.AlreadyKnown).OrderBy(x => x.Position).ToArray();
        var rated = eligible.Where(x => x.Rating is >= 1 and <= 5).ToArray();
        var top10 = eligible.Take(10).ToArray();
        var accepted = rated.Count(x => x.Rating >= 4); var rejected = rated.Count(x => x.Rating <= 2);
        return new(rows.Count, rated.Length, rows.Count(x => x.AlreadyKnown), accepted, rejected,
            top10.Length == 10 && top10.All(x => x.Rating.HasValue) ? top10.Count(x => x.Rating >= 4) / 10d : null,
            rated.Length == 0 ? null : rated.Average(x => x.Rating!.Value), Rate(accepted, rated.Length), Rate(rejected, rated.Length),
            new[] { "HighConfidence", "Discovery", "Wildcard" }.Select(b => { var subset = rated.Where(x => x.Band == b).ToArray(); var n = subset.Count(x => x.Rating >= 4); return new BandMetric(b, subset.Length, n, Rate(n, subset.Length)); }).ToArray());
    }
    private static double? Rate(int n, int d) => d == 0 ? null : n / (double)d;
}
