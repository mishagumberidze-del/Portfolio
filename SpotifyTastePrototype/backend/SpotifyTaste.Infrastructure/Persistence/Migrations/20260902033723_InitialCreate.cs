using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyTaste.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artists",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpotifyArtistId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artists", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RecordsFound = table.Column<int>(type: "int", nullable: false),
                    RecordsImported = table.Column<int>(type: "int", nullable: false),
                    Duplicates = table.Column<int>(type: "int", nullable: false),
                    InvalidRecords = table.Column<int>(type: "int", nullable: false),
                    IgnoredNonMusicRecords = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "User",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SpotifyUsername = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    HistoryImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SpotifyLibraryDataAvailable = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Albums",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpotifyAlbumId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    PrimaryArtistId = table.Column<long>(type: "bigint", nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReleaseYear = table.Column<short>(type: "smallint", nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Albums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Albums_Artists_PrimaryArtistId",
                        column: x => x.PrimaryArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ImportErrors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportBatchId = table.Column<long>(type: "bigint", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RecordNumber = table.Column<int>(type: "int", nullable: true),
                    ErrorType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportErrors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportErrors_ImportBatches_ImportBatchId",
                        column: x => x.ImportBatchId,
                        principalTable: "ImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecommendationRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ListType = table.Column<int>(type: "int", nullable: false),
                    ModelVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CandidateProvider = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationRuns_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Tracks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SpotifyTrackId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    SpotifyUri = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ISRC = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    AlbumId = table.Column<long>(type: "bigint", nullable: true),
                    DurationMs = table.Column<int>(type: "int", nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    SpotifyUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EnrichmentStatus = table.Column<int>(type: "int", nullable: false),
                    EnrichmentLastAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EnrichmentFailureReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tracks_Albums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "Albums",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ListeningEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TrackId = table.Column<long>(type: "bigint", nullable: false),
                    PlayedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    MsPlayed = table.Column<int>(type: "int", nullable: false),
                    WasSkipped = table.Column<bool>(type: "bit", nullable: true),
                    Shuffle = table.Column<bool>(type: "bit", nullable: true),
                    Offline = table.Column<bool>(type: "bit", nullable: true),
                    PrivateSession = table.Column<bool>(type: "bit", nullable: true),
                    Platform = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    StartReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EndReason = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExternalEventHash = table.Column<string>(type: "nchar(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningEvents_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ListeningEvents_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RecommendationCandidates",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecommendationRunId = table.Column<long>(type: "bigint", nullable: false),
                    TrackId = table.Column<long>(type: "bigint", nullable: false),
                    Rank = table.Column<short>(type: "smallint", nullable: false),
                    Band = table.Column<int>(type: "int", nullable: false),
                    FinalScore = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    AudioFeatureSimilarity = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    RecentTasteSimilarity = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    LifetimeTasteSimilarity = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    ArtistAffinity = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    HistoricalBehaviorSimilarity = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    SavedTrackSimilarity = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    NoveltyScore = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    ExplorationScore = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    Explanation = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecommendationCandidates_RecommendationRuns_RecommendationRunId",
                        column: x => x.RecommendationRunId,
                        principalTable: "RecommendationRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecommendationCandidates_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TrackArtists",
                columns: table => new
                {
                    TrackId = table.Column<long>(type: "bigint", nullable: false),
                    ArtistId = table.Column<long>(type: "bigint", nullable: false),
                    ArtistOrder = table.Column<short>(type: "smallint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackArtists", x => new { x.TrackId, x.ArtistId });
                    table.ForeignKey(
                        name: "FK_TrackArtists_Artists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "Artists",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TrackArtists_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TrackExternalIds",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrackId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ExternalId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackExternalIds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackExternalIds_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TrackFeatures",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TrackId = table.Column<long>(type: "bigint", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Tempo = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    Energy = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    Danceability = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    Valence = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    Acousticness = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    Instrumentalness = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    Speechiness = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    Liveness = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    Loudness = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: true),
                    Key = table.Column<short>(type: "smallint", nullable: true),
                    Mode = table.Column<short>(type: "smallint", nullable: true),
                    TimeSignature = table.Column<short>(type: "smallint", nullable: true),
                    FetchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackFeatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackFeatures_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TrackGenres",
                columns: table => new
                {
                    TrackId = table.Column<long>(type: "bigint", nullable: false),
                    GenreId = table.Column<long>(type: "bigint", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackGenres", x => new { x.TrackId, x.GenreId, x.Source });
                    table.ForeignKey(
                        name: "FK_TrackGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TrackGenres_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TrackPreferences",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TrackId = table.Column<long>(type: "bigint", nullable: false),
                    ModelVersion = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PositiveWeight = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    NegativeWeight = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    RecencyWeight = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    RepeatWeight = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    CompletionWeight = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    SkipPenalty = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    SavedBoost = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    BehavioralScore = table.Column<decimal>(type: "decimal(12,6)", precision: 12, scale: 6, nullable: false),
                    CalculatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackPreferences_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TrackPreferences_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserSavedTracks",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    TrackId = table.Column<long>(type: "bigint", nullable: false),
                    SavedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ImportedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSavedTracks", x => new { x.UserId, x.TrackId });
                    table.ForeignKey(
                        name: "FK_UserSavedTracks_Tracks_TrackId",
                        column: x => x.TrackId,
                        principalTable: "Tracks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_UserSavedTracks_User_UserId",
                        column: x => x.UserId,
                        principalTable: "User",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Albums_NormalizedName_PrimaryArtistId",
                table: "Albums",
                columns: new[] { "NormalizedName", "PrimaryArtistId" });

            migrationBuilder.CreateIndex(
                name: "IX_Albums_PrimaryArtistId",
                table: "Albums",
                column: "PrimaryArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_Albums_SpotifyAlbumId",
                table: "Albums",
                column: "SpotifyAlbumId",
                unique: true,
                filter: "[SpotifyAlbumId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_NormalizedName",
                table: "Artists",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_Artists_SpotifyArtistId",
                table: "Artists",
                column: "SpotifyArtistId",
                unique: true,
                filter: "[SpotifyArtistId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Genres_NormalizedName",
                table: "Genres",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_StartedAtUtc",
                table: "ImportBatches",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ImportErrors_ImportBatchId",
                table: "ImportErrors",
                column: "ImportBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningEvents_ExternalEventHash",
                table: "ListeningEvents",
                column: "ExternalEventHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ListeningEvents_TrackId_PlayedAtUtc",
                table: "ListeningEvents",
                columns: new[] { "TrackId", "PlayedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningEvents_UserId_PlayedAtUtc",
                table: "ListeningEvents",
                columns: new[] { "UserId", "PlayedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ListeningEvents_UserId_TrackId_PlayedAtUtc",
                table: "ListeningEvents",
                columns: new[] { "UserId", "TrackId", "PlayedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationCandidates_RecommendationRunId_Rank",
                table: "RecommendationCandidates",
                columns: new[] { "RecommendationRunId", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationCandidates_RecommendationRunId_TrackId",
                table: "RecommendationCandidates",
                columns: new[] { "RecommendationRunId", "TrackId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationCandidates_TrackId",
                table: "RecommendationCandidates",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRuns_UserId",
                table: "RecommendationRuns",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackArtists_ArtistId",
                table: "TrackArtists",
                column: "ArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackArtists_TrackId_ArtistOrder",
                table: "TrackArtists",
                columns: new[] { "TrackId", "ArtistOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackExternalIds_Provider_ExternalId",
                table: "TrackExternalIds",
                columns: new[] { "Provider", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackExternalIds_TrackId_Provider",
                table: "TrackExternalIds",
                columns: new[] { "TrackId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackFeatures_TrackId_Provider",
                table: "TrackFeatures",
                columns: new[] { "TrackId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackGenres_GenreId",
                table: "TrackGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackPreferences_TrackId",
                table: "TrackPreferences",
                column: "TrackId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackPreferences_UserId_TrackId_ModelVersion",
                table: "TrackPreferences",
                columns: new[] { "UserId", "TrackId", "ModelVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_AlbumId",
                table: "Tracks",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_EnrichmentStatus",
                table: "Tracks",
                column: "EnrichmentStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_ISRC",
                table: "Tracks",
                column: "ISRC",
                filter: "[ISRC] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_NormalizedName_AlbumId",
                table: "Tracks",
                columns: new[] { "NormalizedName", "AlbumId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_SpotifyTrackId",
                table: "Tracks",
                column: "SpotifyTrackId",
                unique: true,
                filter: "[SpotifyTrackId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Tracks_SpotifyUri",
                table: "Tracks",
                column: "SpotifyUri",
                unique: true,
                filter: "[SpotifyUri] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserSavedTracks_TrackId",
                table: "UserSavedTracks",
                column: "TrackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImportErrors");

            migrationBuilder.DropTable(
                name: "ListeningEvents");

            migrationBuilder.DropTable(
                name: "RecommendationCandidates");

            migrationBuilder.DropTable(
                name: "TrackArtists");

            migrationBuilder.DropTable(
                name: "TrackExternalIds");

            migrationBuilder.DropTable(
                name: "TrackFeatures");

            migrationBuilder.DropTable(
                name: "TrackGenres");

            migrationBuilder.DropTable(
                name: "TrackPreferences");

            migrationBuilder.DropTable(
                name: "UserSavedTracks");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "RecommendationRuns");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Tracks");

            migrationBuilder.DropTable(
                name: "User");

            migrationBuilder.DropTable(
                name: "Albums");

            migrationBuilder.DropTable(
                name: "Artists");
        }
    }
}
