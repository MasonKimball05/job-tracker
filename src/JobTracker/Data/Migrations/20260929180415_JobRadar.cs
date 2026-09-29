using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class JobRadar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RadarPostings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Board = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CompanyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Company = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Location = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Remote = table.Column<bool>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    PostedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Closed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    FilterReason = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Score = table.Column<int>(type: "INTEGER", nullable: true),
                    Verdict = table.Column<string>(type: "TEXT", nullable: true),
                    Reasons = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    BatchId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ScoredAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Notified = table.Column<bool>(type: "INTEGER", nullable: false),
                    JobApplicationId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadarPostings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RadarRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FetchedBoards = table.Column<bool>(type: "INTEGER", nullable: false),
                    Fetched = table.Column<int>(type: "INTEGER", nullable: false),
                    New = table.Column<int>(type: "INTEGER", nullable: false),
                    Passed = table.Column<int>(type: "INTEGER", nullable: false),
                    Submitted = table.Column<int>(type: "INTEGER", nullable: false),
                    Scored = table.Column<int>(type: "INTEGER", nullable: false),
                    Errors = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadarRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RadarPostings_Board_CompanyKey_ExternalId",
                table: "RadarPostings",
                columns: new[] { "Board", "CompanyKey", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadarPostings_Status",
                table: "RadarPostings",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RadarPostings");

            migrationBuilder.DropTable(
                name: "RadarRuns");
        }
    }
}
