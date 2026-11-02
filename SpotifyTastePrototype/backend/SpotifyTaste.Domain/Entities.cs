namespace SpotifyTaste.Domain;

public abstract class Entity
{
    public long Id { get; set; }
}

public sealed class User : Entity
{
    public string DisplayName { get; set; } = "Local User";
    public string? SpotifyUsername { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? HistoryImportedAtUtc { get; set; }
    public bool SpotifyLibraryDataAvailable { get; set; }
}

public sealed class Artist : Entity
{
    public string? SpotifyArtistId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class Album : Entity
{
    public string? SpotifyAlbumId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public long? PrimaryArtistId { get; set; }
    public Artist? PrimaryArtist { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public short? ReleaseYear { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class Track : Entity
{
    public string? SpotifyTrackId { get; set; }
    public string? SpotifyUri { get; set; }
    public string? Isrc { get; set; }
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public long? AlbumId { get; set; }
    public Album? Album { get; set; }
    public int? DurationMs { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public string? SpotifyUrl { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public EnrichmentStatus EnrichmentStatus { get; set; } = EnrichmentStatus.Pending;
    public DateTime? EnrichmentLastAttemptAtUtc { get; set; }
    public string? EnrichmentFailureReason { get; set; }
    public ICollection<TrackArtist> TrackArtists { get; set; } = [];
}

public sealed class TrackArtist
{
    public long TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public long ArtistId { get; set; }
    public Artist Artist { get; set; } = null!;
    public short ArtistOrder { get; set; }
}

public sealed class Genre : Entity
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
}

public sealed class TrackGenre
{
    public long TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public long GenreId { get; set; }
    public Genre Genre { get; set; } = null!;
    public string Source { get; set; } = string.Empty;
}

public sealed class ListeningEvent : Entity
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public long TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public DateTime PlayedAtUtc { get; set; }
    public int MsPlayed { get; set; }
    public bool? WasSkipped { get; set; }
    public bool? Shuffle { get; set; }
    public bool? Offline { get; set; }
    public bool? PrivateSession { get; set; }
    public string? Platform { get; set; }
    public string? StartReason { get; set; }
    public string? EndReason { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime ImportedAtUtc { get; set; }
    public string ExternalEventHash { get; set; } = string.Empty;
}

public sealed class UserSavedTrack
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public long TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public DateTime? SavedAtUtc { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTime ImportedAtUtc { get; set; }
}

public sealed class TrackExternalId : Entity
{
    public long TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public string Provider { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class TrackFeature : Entity
{
    public long TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public string Provider { get; set; } = string.Empty;
    public decimal? Tempo { get; set; }
    public decimal? Energy { get; set; }
    public decimal? Danceability { get; set; }
    public decimal? Valence { get; set; }
    public decimal? Acousticness { get; set; }
    public decimal? Instrumentalness { get; set; }
    public decimal? Speechiness { get; set; }
    public decimal? Liveness { get; set; }
    public decimal? Loudness { get; set; }
    public short? Key { get; set; }
    public short? Mode { get; set; }
    public short? TimeSignature { get; set; }
    public DateTime FetchedAtUtc { get; set; }
}

public sealed class ImportBatch : Entity
{
    public string Source { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTime StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int RecordsFound { get; set; }
    public int RecordsImported { get; set; }
    public int Duplicates { get; set; }
    public int InvalidRecords { get; set; }
    public int IgnoredNonMusicRecords { get; set; }
    public ImportBatchStatus Status { get; set; }
    public ICollection<ImportError> Errors { get; set; } = [];
}

public sealed class ImportError : Entity
{
    public long ImportBatchId { get; set; }
    public ImportBatch ImportBatch { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;
    public int? RecordNumber { get; set; }
    public string ErrorType { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
}

public sealed class TrackPreference : Entity
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public long TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public string ModelVersion { get; set; } = string.Empty;
    public decimal PositiveWeight { get; set; }
    public decimal NegativeWeight { get; set; }
    public decimal RecencyWeight { get; set; }
    public decimal RepeatWeight { get; set; }
    public decimal CompletionWeight { get; set; }
    public decimal SkipPenalty { get; set; }
    public decimal SavedBoost { get; set; }
    public decimal BehavioralScore { get; set; }
    public DateTime CalculatedAtUtc { get; set; }
}

public sealed class RecommendationRun : Entity
{
    public long UserId { get; set; }
    public User User { get; set; } = null!;
    public RecommendationListType ListType { get; set; }
    public string ModelVersion { get; set; } = string.Empty;
    public string CandidateProvider { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<RecommendationCandidate> Candidates { get; set; } = [];
}

public sealed class RecommendationCandidate : Entity
{
    public string Confidence { get; set; } = "Unknown";
    public string? ArtworkUrl { get; set; }
    public string? AlbumName { get; set; }
    public long RecommendationRunId { get; set; }
    public RecommendationRun RecommendationRun { get; set; } = null!;
    public long TrackId { get; set; }
    public Track Track { get; set; } = null!;
    public short Rank { get; set; }
    public RecommendationBand Band { get; set; }
    public decimal FinalScore { get; set; }
    public decimal AudioFeatureSimilarity { get; set; }
    public decimal RecentTasteSimilarity { get; set; }
    public decimal LifetimeTasteSimilarity { get; set; }
    public decimal ArtistAffinity { get; set; }
    public decimal HistoricalBehaviorSimilarity { get; set; }
    public decimal SavedTrackSimilarity { get; set; }
    public decimal NoveltyScore { get; set; }
    public decimal ExplorationScore { get; set; }
    public string Explanation { get; set; } = string.Empty;
}

// Explicit local labels, not Spotify saved state and never listening facts.
public sealed class RecommendationFeedback : Entity
{
    public long RecommendationCandidateId { get; set; }
    public RecommendationCandidate RecommendationCandidate { get; set; } = null!;
    public long? EvaluationItemId { get; set; }
    public EvaluationItem? EvaluationItem { get; set; }
    public string Kind { get; set; } = string.Empty;
    public byte? Rating { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
public sealed class EvaluationSession : Entity
{
    public int TargetCount { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public ICollection<EvaluationItem> Items { get; set; } = [];
}
public sealed class EvaluationItem : Entity
{
    public long EvaluationSessionId { get; set; }
    public EvaluationSession EvaluationSession { get; set; } = null!;
    public long RecommendationCandidateId { get; set; }
    public RecommendationCandidate RecommendationCandidate { get; set; } = null!;
    public int Position { get; set; }
}
