using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimBridge.Scrubber.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScrubRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScrubRuns",
                schema: "scrubber",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimVersion = table.Column<int>(type: "int", nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RanAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Passed = table.Column<bool>(type: "bit", nullable: false),
                    ErrorCount = table.Column<int>(type: "int", nullable: false),
                    WarningCount = table.Column<int>(type: "int", nullable: false),
                    RulesEvaluated = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrubRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScrubEdits",
                schema: "scrubber",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RunId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RuleCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Field = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    FixSuggestion = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    LineNumber = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrubEdits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScrubEdits_ScrubRuns_RunId",
                        column: x => x.RunId,
                        principalSchema: "scrubber",
                        principalTable: "ScrubRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScrubEdits_RuleCode",
                schema: "scrubber",
                table: "ScrubEdits",
                column: "RuleCode");

            migrationBuilder.CreateIndex(
                name: "IX_ScrubEdits_RunId",
                schema: "scrubber",
                table: "ScrubEdits",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_ScrubRuns_ClaimId_RanAt",
                schema: "scrubber",
                table: "ScrubRuns",
                columns: new[] { "ClaimId", "RanAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScrubEdits",
                schema: "scrubber");

            migrationBuilder.DropTable(
                name: "ScrubRuns",
                schema: "scrubber");
        }
    }
}
