using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimBridge.PayersGateway.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayerFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence<int>(
                name: "InterchangeControlNumbers",
                schema: "payers",
                startValue: 1000L);

            migrationBuilder.CreateTable(
                name: "Files",
                schema: "payers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    BlobContainer = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ControlNumber = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: true),
                    ClaimCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Files_PayerId_Kind_ControlNumber",
                schema: "payers",
                table: "Files",
                columns: new[] { "PayerId", "Kind", "ControlNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Files",
                schema: "payers");

            migrationBuilder.DropSequence(
                name: "InterchangeControlNumbers",
                schema: "payers");
        }
    }
}
