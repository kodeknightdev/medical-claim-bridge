using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimBridge.Scrubber.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCodeSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scrubber");

            migrationBuilder.CreateTable(
                name: "IcdCodes",
                schema: "scrubber",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Billable = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IcdCodes", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "MedicalNecessity",
                schema: "scrubber",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcedureCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    DxPrefix = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicalNecessity", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Modifiers",
                schema: "scrubber",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AllowedCategories = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Modifiers", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "PayerSettings",
                schema: "scrubber",
                columns: table => new
                {
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TimelyFilingDays = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayerSettings", x => x.PayerId);
                });

            migrationBuilder.CreateTable(
                name: "ProcedureCodes",
                schema: "scrubber",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TypicalCharge = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcedureCodes", x => x.Code);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicalNecessity_ProcedureCode_DxPrefix",
                schema: "scrubber",
                table: "MedicalNecessity",
                columns: new[] { "ProcedureCode", "DxPrefix" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IcdCodes",
                schema: "scrubber");

            migrationBuilder.DropTable(
                name: "MedicalNecessity",
                schema: "scrubber");

            migrationBuilder.DropTable(
                name: "Modifiers",
                schema: "scrubber");

            migrationBuilder.DropTable(
                name: "PayerSettings",
                schema: "scrubber");

            migrationBuilder.DropTable(
                name: "ProcedureCodes",
                schema: "scrubber");
        }
    }
}
