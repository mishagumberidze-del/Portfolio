using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SpotifyTaste.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationFeedbackAndEvaluation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AlbumName",
                table: "RecommendationCandidates",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArtworkUrl",
                table: "RecommendationCandidates",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Confidence",
                table: "RecommendationCandidates",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.CreateTable(
                name: "EvaluationSessions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TargetCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationSessions", x => x.Id);
                    table.CheckConstraint("CK_EvaluationSession_Target", "[TargetCount] BETWEEN 50 AND 100");
                });

            migrationBuilder.CreateTable(
                name: "EvaluationItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EvaluationSessionId = table.Column<long>(type: "bigint", nullable: false),
                    RecommendationCandidateId = table.Column<long>(type: "bigint", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationItems_EvaluationSessions_EvaluationSessionId",
                        column: x => x.EvaluationSessionId,
                        principalTable: "EvaluationSessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EvaluationItems_RecommendationCandidates_RecommendationCandidateId",
                        column: x => x.RecommendationCandidateId,
                        principalTable: "RecommendationCandidates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RecommendationFeedback",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RecommendationCandidateId = table.Column<long>(type: "bigint", nullable: false),
                    EvaluationItemId = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Rating = table.Column<byte>(type: "tinyint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationFeedback", x => x.Id);
                    table.CheckConstraint("CK_Feedback_Rating", "[Rating] IS NULL OR [Rating] BETWEEN 1 AND 5");
                    table.ForeignKey(
                        name: "FK_RecommendationFeedback_EvaluationItems_EvaluationItemId",
                        column: x => x.EvaluationItemId,
                        principalTable: "EvaluationItems",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RecommendationFeedback_RecommendationCandidates_RecommendationCandidateId",
                        column: x => x.RecommendationCandidateId,
                        principalTable: "RecommendationCandidates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationItems_EvaluationSessionId_Position",
                table: "EvaluationItems",
                columns: new[] { "EvaluationSessionId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationItems_RecommendationCandidateId",
                table: "EvaluationItems",
                column: "RecommendationCandidateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationSessions_CreatedAtUtc",
                table: "EvaluationSessions",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationFeedback_EvaluationItemId",
                table: "RecommendationFeedback",
                column: "EvaluationItemId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationFeedback_RecommendationCandidateId_CreatedAtUtc",
                table: "RecommendationFeedback",
                columns: new[] { "RecommendationCandidateId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecommendationFeedback");

            migrationBuilder.DropTable(
                name: "EvaluationItems");

            migrationBuilder.DropTable(
                name: "EvaluationSessions");

            migrationBuilder.DropColumn(
                name: "AlbumName",
                table: "RecommendationCandidates");

            migrationBuilder.DropColumn(
                name: "ArtworkUrl",
                table: "RecommendationCandidates");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "RecommendationCandidates");
        }
    }
}
