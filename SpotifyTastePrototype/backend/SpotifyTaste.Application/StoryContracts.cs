namespace SpotifyTaste.Application;

public sealed record StorySummary(long TotalMs, int Events, int QualifiedPlays, int ActiveDays,
    int Recordings, int Artists, int Albums, DateTime? FirstListenUtc, DateTime? LastListenUtc);
public sealed record StoryRanking(long Id, string Name, string? Artist, string? Album, string? ArtworkUrl,
    long TotalMs, int Events, int QualifiedPlays, DateTime FirstListenUtc, string PeakMonth, int? PreviousRank, string? SpotifyUrl = null);
public sealed record StoryMonth(string Period, long TotalMs, string? DefiningArtist);
public sealed record SoundAxis(string Name, double? Value, string Unit, double CoveragePercent, double? PersonalPercentile);
public sealed record SoundEra(int Year, IReadOnlyList<SoundAxis> Axes);
public sealed record StoryComparison(int Year, long TotalMs, double? ChangePercent, string Note);
public sealed record StoryFact(string Title, string Detail);
public sealed record StoryDto(int? Year, IReadOnlyList<int> AvailableYears, StorySummary Summary,
    IReadOnlyList<StoryRanking> TopArtists, IReadOnlyList<StoryRanking> TopSongs, IReadOnlyList<StoryRanking> TopAlbums,
    IReadOnlyList<StoryMonth> Journey, int NewRecordings, int NewArtists, BehaviorAnalyticsDto Behavior,
    IReadOnlyList<SoundAxis> SoundProfile, IReadOnlyList<SoundEra> FeatureEvolution,
    StoryComparison? Comparison, IReadOnlyList<StoryFact> Facts, IReadOnlyList<string> FeatureInsights);
public interface IStoryService
{
    Task<StoryDto> GetAsync(int? year, CancellationToken cancellationToken = default);
}
