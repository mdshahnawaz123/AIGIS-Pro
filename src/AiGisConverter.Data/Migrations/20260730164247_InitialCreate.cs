using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AiGisConverter.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Settings = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ModifiedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Settings = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ResolvedCoordinateSystem = table.Column<string>(type: "TEXT", nullable: true),
                    CrsSource = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ElementsRead = table.Column<int>(type: "INTEGER", nullable: false),
                    FeaturesWritten = table.Column<int>(type: "INTEGER", nullable: false),
                    HighestSeverity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    IssueCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    OutputPaths = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValidationIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    DetectedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Layer = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    FeatureId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    LocationX = table.Column<double>(type: "REAL", nullable: true),
                    LocationY = table.Column<double>(type: "REAL", nullable: true),
                    Remediation = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationIssues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LatestRunId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Jobs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_ProjectId",
                table: "Jobs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Status",
                table: "Jobs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_ModifiedAtUtc",
                table: "Projects",
                column: "ModifiedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Runs_FinishedAtUtc",
                table: "Runs",
                column: "FinishedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_JobId",
                table: "Runs",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_ProjectId",
                table: "Runs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_Status",
                table: "Runs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationIssues_Code",
                table: "ValidationIssues",
                column: "Code");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationIssues_RunId",
                table: "ValidationIssues",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationIssues_Severity",
                table: "ValidationIssues",
                column: "Severity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "ValidationIssues");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
