using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimBridge.Claims.Data.Migrations
{
    /// <inheritdoc />
    public partial class ServiceLineNumbersNotIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQL Server can't drop IDENTITY in place; nothing is live yet, so recreate the table
            migrationBuilder.DropTable(name: "ServiceLines", schema: "claims");

            migrationBuilder.CreateTable(
                name: "ServiceLines",
                schema: "claims",
                columns: table => new
                {
                    LineNumber = table.Column<int>(type: "int", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Modifiers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Units = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                    Charge = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlaceOfService = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    DxPointers = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLines", x => new { x.ClaimId, x.LineNumber });
                    table.ForeignKey(
                        name: "FK_ServiceLines_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalSchema: "claims",
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ServiceLines", schema: "claims");

            migrationBuilder.CreateTable(
                name: "ServiceLines",
                schema: "claims",
                columns: table => new
                {
                    LineNumber = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcedureCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Modifiers = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Units = table.Column<decimal>(type: "decimal(7,2)", precision: 7, scale: 2, nullable: false),
                    Charge = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ServiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlaceOfService = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    DxPointers = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLines", x => new { x.ClaimId, x.LineNumber });
                    table.ForeignKey(
                        name: "FK_ServiceLines_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalSchema: "claims",
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }
    }
}
