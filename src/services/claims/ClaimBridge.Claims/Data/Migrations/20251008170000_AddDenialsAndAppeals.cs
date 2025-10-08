using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimBridge.Claims.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDenialsAndAppeals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appeals",
                schema: "claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CarcCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Argument = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Enclosures = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Deadline = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OutcomeAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    OutcomeNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    LetterBlobName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appeals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DenialWorklist",
                schema: "claims",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CarcCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    DeniedAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DeniedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AppealDeadline = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    CorrectedByClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AppealId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DenialWorklist", x => x.ClaimId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appeals_ClaimId",
                schema: "claims",
                table: "Appeals",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_Appeals_OrgId_Status",
                schema: "claims",
                table: "Appeals",
                columns: new[] { "OrgId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DenialWorklist_OrgId_Status",
                schema: "claims",
                table: "DenialWorklist",
                columns: new[] { "OrgId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Appeals",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "DenialWorklist",
                schema: "claims");
        }
    }
}
