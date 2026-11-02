using Microsoft.EntityFrameworkCore;
using SpotifyTaste.Domain;

namespace SpotifyTaste.Infrastructure.Persistence;

public sealed class SpotifyTasteDbContext(DbContextOptions<SpotifyTasteDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<Album> Albums => Set<Album>();
    public DbSet<Track> Tracks => Set<Track>();
    public DbSet<TrackArtist> TrackArtists => Set<TrackArtist>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<TrackGenre> TrackGenres => Set<TrackGenre>();
    public DbSet<ListeningEvent> ListeningEvents => Set<ListeningEvent>();
    public DbSet<UserSavedTrack> UserSavedTracks => Set<UserSavedTrack>();
    public DbSet<TrackExternalId> TrackExternalIds => Set<TrackExternalId>();
    public DbSet<TrackFeature> TrackFeatures => Set<TrackFeature>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public DbSet<ImportError> ImportErrors => Set<ImportError>();
    public DbSet<TrackPreference> TrackPreferences => Set<TrackPreference>();
    public DbSet<RecommendationRun> RecommendationRuns => Set<RecommendationRun>();
    public DbSet<RecommendationCandidate> RecommendationCandidates => Set<RecommendationCandidate>();
    public DbSet<RecommendationFeedback> RecommendationFeedback => Set<RecommendationFeedback>();
    public DbSet<EvaluationSession> EvaluationSessions => Set<EvaluationSession>();
    public DbSet<EvaluationItem> EvaluationItems => Set<EvaluationItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUser(modelBuilder);
        ConfigureMusic(modelBuilder);
        ConfigureFacts(modelBuilder);
        ConfigureProviders(modelBuilder);
        ConfigureImports(modelBuilder);
        ConfigureDerived(modelBuilder);
        ConfigureFeedback(modelBuilder);
    }

    private static void ConfigureUser(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("User");
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.SpotifyUsername).HasMaxLength(200);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.HistoryImportedAtUtc).HasColumnType("datetime2");
        });
    }

    private static void ConfigureMusic(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Artist>(entity =>
        {
            entity.Property(x => x.SpotifyArtistId).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(300).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(300).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2");
            entity.HasIndex(x => x.SpotifyArtistId).IsUnique().HasFilter("[SpotifyArtistId] IS NOT NULL");
            entity.HasIndex(x => x.NormalizedName);
        });

        modelBuilder.Entity<Album>(entity =>
        {
            entity.Property(x => x.SpotifyAlbumId).HasMaxLength(32);
            entity.Property(x => x.Name).HasMaxLength(400).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(400).IsRequired();
            entity.Property(x => x.ImageUrl).HasMaxLength(1000);
            entity.Property(x => x.ReleaseDate).HasColumnType("date");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2");
            entity.HasOne(x => x.PrimaryArtist).WithMany().HasForeignKey(x => x.PrimaryArtistId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => x.SpotifyAlbumId).IsUnique().HasFilter("[SpotifyAlbumId] IS NOT NULL");
            entity.HasIndex(x => new { x.NormalizedName, x.PrimaryArtistId });
        });

        modelBuilder.Entity<Track>(entity =>
        {
            entity.Property(x => x.SpotifyTrackId).HasMaxLength(32);
            entity.Property(x => x.SpotifyUri).HasMaxLength(100);
            entity.Property(x => x.Isrc).HasColumnName("ISRC").HasMaxLength(16);
            entity.Property(x => x.Name).HasMaxLength(500).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(500).IsRequired();
            entity.Property(x => x.ReleaseDate).HasColumnType("date");
            entity.Property(x => x.SpotifyUrl).HasMaxLength(500);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.EnrichmentLastAttemptAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.EnrichmentFailureReason).HasMaxLength(500);
            entity.HasOne(x => x.Album).WithMany().HasForeignKey(x => x.AlbumId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => x.SpotifyTrackId).IsUnique().HasFilter("[SpotifyTrackId] IS NOT NULL");
            entity.HasIndex(x => x.SpotifyUri).IsUnique().HasFilter("[SpotifyUri] IS NOT NULL");
            entity.HasIndex(x => x.Isrc).HasFilter("[ISRC] IS NOT NULL");
            entity.HasIndex(x => new { x.NormalizedName, x.AlbumId });
            entity.HasIndex(x => x.EnrichmentStatus);
        });

        modelBuilder.Entity<TrackArtist>(entity =>
        {
            entity.HasKey(x => new { x.TrackId, x.ArtistId });
            entity.HasIndex(x => new { x.TrackId, x.ArtistOrder }).IsUnique();
            entity.HasOne(x => x.Track).WithMany(x => x.TrackArtists).HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Artist).WithMany().HasForeignKey(x => x.ArtistId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Genre>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(120).IsRequired();
            entity.Property(x => x.NormalizedName).HasMaxLength(120).IsRequired();
            entity.HasIndex(x => x.NormalizedName).IsUnique();
        });

        modelBuilder.Entity<TrackGenre>(entity =>
        {
            entity.HasKey(x => new { x.TrackId, x.GenreId, x.Source });
            entity.Property(x => x.Source).HasMaxLength(40);
            entity.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Genre).WithMany().HasForeignKey(x => x.GenreId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureFacts(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ListeningEvent>(entity =>
        {
            entity.Property(x => x.PlayedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.Platform).HasMaxLength(200);
            entity.Property(x => x.StartReason).HasMaxLength(100);
            entity.Property(x => x.EndReason).HasMaxLength(100);
            entity.Property(x => x.Source).HasMaxLength(60).IsRequired();
            entity.Property(x => x.ImportedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.ExternalEventHash).HasMaxLength(64).IsFixedLength().IsRequired();
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => x.ExternalEventHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.PlayedAtUtc });
            entity.HasIndex(x => new { x.UserId, x.TrackId, x.PlayedAtUtc });
            entity.HasIndex(x => new { x.TrackId, x.PlayedAtUtc });
        });

        modelBuilder.Entity<UserSavedTrack>(entity =>
        {
            entity.HasKey(x => new { x.UserId, x.TrackId });
            entity.Property(x => x.SavedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.Source).HasMaxLength(60);
            entity.Property(x => x.ImportedAtUtc).HasColumnType("datetime2");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.NoAction);
        });
    }

    private static void ConfigureProviders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackExternalId>(entity =>
        {
            entity.Property(x => x.Provider).HasMaxLength(40).IsRequired();
            entity.Property(x => x.ExternalId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            entity.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => new { x.Provider, x.ExternalId }).IsUnique();
            entity.HasIndex(x => new { x.TrackId, x.Provider }).IsUnique();
        });

        modelBuilder.Entity<TrackFeature>(entity =>
        {
            entity.Property(x => x.Provider).HasMaxLength(40).IsRequired();
            ConfigureDecimals(entity, nameof(TrackFeature.Tempo), nameof(TrackFeature.Energy), nameof(TrackFeature.Danceability), nameof(TrackFeature.Valence), nameof(TrackFeature.Acousticness), nameof(TrackFeature.Instrumentalness), nameof(TrackFeature.Speechiness), nameof(TrackFeature.Liveness), nameof(TrackFeature.Loudness));
            entity.Property(x => x.FetchedAtUtc).HasColumnType("datetime2");
            entity.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => new { x.TrackId, x.Provider }).IsUnique();
        });
    }

    private static void ConfigureImports(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ImportBatch>(entity =>
        {
            entity.Property(x => x.Source).HasMaxLength(60);
            entity.Property(x => x.FileName).HasMaxLength(500);
            entity.Property(x => x.StartedAtUtc).HasColumnType("datetime2");
            entity.Property(x => x.CompletedAtUtc).HasColumnType("datetime2");
            entity.HasIndex(x => x.StartedAtUtc);
        });
        modelBuilder.Entity<ImportError>(entity =>
        {
            entity.Property(x => x.FileName).HasMaxLength(500);
            entity.Property(x => x.ErrorType).HasMaxLength(100);
            entity.Property(x => x.ErrorMessage).HasMaxLength(2000);
            entity.HasOne(x => x.ImportBatch).WithMany(x => x.Errors).HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureDerived(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TrackPreference>(entity =>
        {
            entity.Property(x => x.ModelVersion).HasMaxLength(64);
            ConfigureDecimals(entity, nameof(TrackPreference.PositiveWeight), nameof(TrackPreference.NegativeWeight), nameof(TrackPreference.RecencyWeight), nameof(TrackPreference.RepeatWeight), nameof(TrackPreference.CompletionWeight), nameof(TrackPreference.SkipPenalty), nameof(TrackPreference.SavedBoost), nameof(TrackPreference.BehavioralScore));
            entity.Property(x => x.CalculatedAtUtc).HasColumnType("datetime2");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => new { x.UserId, x.TrackId, x.ModelVersion }).IsUnique();
        });
        modelBuilder.Entity<RecommendationRun>(entity =>
        {
            entity.Property(x => x.ModelVersion).HasMaxLength(64);
            entity.Property(x => x.CandidateProvider).HasMaxLength(64);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<RecommendationCandidate>(entity =>
        {
            entity.Property(x => x.Confidence).HasMaxLength(16).HasDefaultValue("Unknown");
            entity.Property(x => x.ArtworkUrl).HasMaxLength(1000);
            entity.Property(x => x.AlbumName).HasMaxLength(400);
            ConfigureDecimals(entity, nameof(RecommendationCandidate.FinalScore), nameof(RecommendationCandidate.AudioFeatureSimilarity), nameof(RecommendationCandidate.RecentTasteSimilarity), nameof(RecommendationCandidate.LifetimeTasteSimilarity), nameof(RecommendationCandidate.ArtistAffinity), nameof(RecommendationCandidate.HistoricalBehaviorSimilarity), nameof(RecommendationCandidate.SavedTrackSimilarity), nameof(RecommendationCandidate.NoveltyScore), nameof(RecommendationCandidate.ExplorationScore));
            entity.Property(x => x.Explanation).HasMaxLength(1000);
            entity.HasOne(x => x.RecommendationRun).WithMany(x => x.Candidates).HasForeignKey(x => x.RecommendationRunId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Track).WithMany().HasForeignKey(x => x.TrackId).OnDelete(DeleteBehavior.NoAction);
            entity.HasIndex(x => new { x.RecommendationRunId, x.Rank }).IsUnique();
            entity.HasIndex(x => new { x.RecommendationRunId, x.TrackId }).IsUnique();
        });
    }

    private static void ConfigureDecimals<TEntity>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TEntity> entity, params string[] properties) where TEntity : class
    {
        foreach (var property in properties) entity.Property(property).HasPrecision(12, 6);
    }

    private static void ConfigureFeedback(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EvaluationSession>(e =>
        {
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            e.HasIndex(x => x.CreatedAtUtc);
            e.ToTable(t => t.HasCheckConstraint("CK_EvaluationSession_Target", "[TargetCount] BETWEEN 50 AND 100"));
        });
        modelBuilder.Entity<EvaluationItem>(e =>
        {
            e.HasOne(x => x.EvaluationSession).WithMany(x => x.Items).HasForeignKey(x => x.EvaluationSessionId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.RecommendationCandidate).WithMany().HasForeignKey(x => x.RecommendationCandidateId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.EvaluationSessionId, x.Position }).IsUnique();
            e.HasIndex(x => x.RecommendationCandidateId).IsUnique();
        });
        modelBuilder.Entity<RecommendationFeedback>(e =>
        {
            e.Property(x => x.Kind).HasMaxLength(24).IsRequired();
            e.Property(x => x.CreatedAtUtc).HasColumnType("datetime2");
            e.HasOne(x => x.RecommendationCandidate).WithMany().HasForeignKey(x => x.RecommendationCandidateId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.EvaluationItem).WithMany().HasForeignKey(x => x.EvaluationItemId).OnDelete(DeleteBehavior.NoAction);
            e.HasIndex(x => new { x.RecommendationCandidateId, x.CreatedAtUtc });
            e.ToTable(t => t.HasCheckConstraint("CK_Feedback_Rating", "[Rating] IS NULL OR [Rating] BETWEEN 1 AND 5"));
        });
    }
}
