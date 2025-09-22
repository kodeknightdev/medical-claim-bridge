using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimBridge.Claims.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClaimSearchReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClaimSearch",
                schema: "claims",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    FrequencyCode = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    MemberIdLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    MemberIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProcedureCodes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    TotalCharge = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    Balance = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    FirstServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    StatusChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DenialRisk = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    DenialCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    HasOpenEdits = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimSearch", x => x.ClaimId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimSearch_ClaimNumber",
                schema: "claims",
                table: "ClaimSearch",
                column: "ClaimNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimSearch_MemberIdHash",
                schema: "claims",
                table: "ClaimSearch",
                column: "MemberIdHash");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimSearch_OrgId_Status_CreatedAt",
                schema: "claims",
                table: "ClaimSearch",
                columns: new[] { "OrgId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimSearch",
                schema: "claims");
        }
    }
}
