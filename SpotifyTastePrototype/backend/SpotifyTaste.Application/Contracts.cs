namespace SpotifyTaste.Application;

public sealed record ImportRequest(string SourcePath);

public sealed record ImportReport(
    long ImportBatchId,
    int RecordsFound,
    int RecordsImported,
    int Duplicates,
    int InvalidRecords,
    int IgnoredNonMusicRecords,
    DateTime? EarliestListenUtc,
    DateTime? LatestListenUtc,
    int UniqueTracks,
    int UniqueArtists,
    int UniqueAlbums,
    bool LibraryDataAvailable,
    int SavedTrackCount);

public interface ISpotifyImportService
{
    Task<ImportReport> ImportAsync(string sourcePath, CancellationToken cancellationToken = default);
}

public sealed record OverviewDto(
    long TotalListeningMinutes,
    decimal TotalListeningHours,
    int TotalListeningDays,
    int ListeningEvents,
    int QualifiedPlays,
    int UniqueTracks,
    int UniqueArtists,
    int UniqueAlbums,
    bool SavedLibraryAvailable,
    int? SavedTracks,
    DateTime? EarliestListenUtc,
    DateTime? LatestListenUtc);

public sealed record TopTrackDto(long TrackId, string Track, string Artist, string? Album, int RawEvents, int QualifiedPlays, decimal ListeningMinutes, decimal AverageListenSeconds, decimal SkipRate, DateTime FirstListenUtc, DateTime LastListenUtc, bool? Saved);
public sealed record TopArtistDto(long ArtistId, string Artist, decimal ListeningMinutes, int QualifiedPlays, int RawPlays, int UniqueTracks, decimal SkipRate, DateTime FirstListenUtc, DateTime LastListenUtc);
public sealed record TopAlbumDto(long AlbumId, string Album, string Artist, decimal ListeningMinutes, int QualifiedPlays, int UniqueTracks);
public sealed record TimelinePointDto(string Period, decimal ListeningMinutes, int UniqueArtists, int NewArtists, int NewTracks);
public sealed record RankEvolutionDto(string Period, long ArtistId, string Artist, int Rank, decimal ListeningMinutes, int QualifiedPlays, string? ArtworkUrl = null);
public sealed record BehaviorAnalyticsDto(int ListeningEvents, decimal SkipRate, decimal ShortListenRate, decimal RepeatRate, decimal? AverageListenPercentage, decimal? CompletionRateEstimate, int EventsWithKnownDuration);

public interface IAnalyticsService
{
    Task<OverviewDto> GetOverviewAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TopTrackDto>> GetTopTracksAsync(DateTime? fromUtc, DateTime? toUtc, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TopArtistDto>> GetTopArtistsAsync(DateTime? fromUtc, DateTime? toUtc, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TopAlbumDto>> GetTopAlbumsAsync(DateTime? fromUtc, DateTime? toUtc, int limit, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimelinePointDto>> GetTimelineAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TimelinePointDto>> GetYearlyTimelineAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<RankEvolutionDto>> GetArtistEvolutionAsync(string grain, CancellationToken cancellationToken = default);
    Task<BehaviorAnalyticsDto> GetBehaviorAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken cancellationToken = default);
}

public sealed record DiagnosticsDto(
    string DatabaseProvider,
    string DatabaseName,
    IReadOnlyList<string> SpotifyDataFiles,
    int HistoryRowsImported,
    bool SavedLibraryAvailable,
    int SavedTrackCount,
    int UniqueTracks,
    int UniqueArtists,
    int UniqueAlbums,
    int TracksWithSpotifyIds,
    int TracksMappedToReccoBeats,
    int TracksWithFeatures,
    int TracksMissingFeatures,
    int ReccoBeatsNotFound,
    string PreferenceModelVersion,
    string CandidateProvider,
    string RecommendationAlgorithmVersion);

public interface IDiagnosticsService
{
    Task<DiagnosticsDto> GetAsync(CancellationToken cancellationToken = default);
}

public sealed record PreferenceValidationDto(int TrackCount, int PositiveExamples, int NegativeExamples, double? MeanPositiveScore, double? MeanNegativeScore, double? PairwiseAccuracy, string ModelVersion, string ValidationMethod);

public interface IPreferenceModelService
{
    Task<PreferenceValidationDto> RebuildAsync(CancellationToken cancellationToken = default);
}

public sealed record TrackFeatureVector(double? Tempo, double? Energy, double? Danceability, double? Valence, double? Acousticness, double? Instrumentalness, double? Speechiness, double? Liveness, double? Loudness, short? Key = null, short? Mode = null, short? TimeSignature = null);
public sealed record ExternalTrack(string ProviderId, string? SpotifyTrackId, string Name, IReadOnlyList<string> Artists, string? Album, int? DurationMs, string? Isrc, string? SpotifyUrl, string? ImageUrl, TrackFeatureVector? Features);

public interface IReccoBeatsClient
{
    Task<ExternalTrack?> GetBySpotifyIdAsync(string spotifyTrackId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ExternalTrack>> GetRecommendationsAsync(IReadOnlyList<string> positiveProviderIds, IReadOnlyList<string> negativeProviderIds, int limit, CancellationToken cancellationToken = default);
}

public interface ITrackFeatureProvider
{
    Task<int> EnrichMissingAsync(int limit, CancellationToken cancellationToken = default);
    Task<TrackEnrichmentBatchResult> EnrichBatchAsync(int limit, bool historicalTracksOnly = false, CancellationToken cancellationToken = default);
}

public sealed record TrackEnrichmentBatchResult(
    int Attempted,
    int Completed,
    int NotFound,
    int RetryLater,
    int RemainingEligible);

public interface ICandidateTrackProvider
{
    string Name { get; }
    Task<IReadOnlyList<ExternalTrack>> GetCandidatesAsync(int desiredCount, CancellationToken cancellationToken = default);
}

public sealed record ScoreComponents(double FeatureMatch, double ArtistTasteMatch, double RecentTasteMatch, double LifetimeTasteMatch, double HistoricalBehaviorMatch, double SavedTrackMatch, double ExplorationAdjustment);
public sealed record ScoreResult(string SpotifyTrackId, string Track, IReadOnlyList<string> Artists, double PredictedLikeScore, string Confidence, ScoreComponents Components, IReadOnlyList<string> Explanations, bool KnownRecording = false, int HistoricalEvents = 0, IReadOnlyList<string>? AvailableSignals = null);

public interface ISongScoringService
{
    Task<ScoreResult> ScoreAsync(string spotifyUrlOrUri, CancellationToken cancellationToken = default);
}

public sealed record RecommendationDto(int Rank, string Track, string Artist, string? ArtworkUrl, double PredictedLikeScore, string Confidence, string Why, string HistoricalStatus, string? SpotifyUrl, string Band, long CandidateId = 0, string? Album = null, string? Feedback = null);
public sealed record DiscoverDto(bool SavedLibraryAvailable, string? NotSavedMessage, IReadOnlyList<RecommendationDto> NotSaved, IReadOnlyList<RecommendationDto> NeverHeard);

public interface IRecommendationService
{
    Task<DiscoverDto> GenerateAsync(int countPerList, CancellationToken cancellationToken = default);
}
