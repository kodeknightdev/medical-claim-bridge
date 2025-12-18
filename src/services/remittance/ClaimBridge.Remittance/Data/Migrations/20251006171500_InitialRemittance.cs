using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimBridge.Remittance.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialRemittance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "remittance");

            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "remittance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClaimBalances",
                schema: "remittance",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Charge = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Paid = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Adjusted = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PatientResponsibility = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PayerOutstanding = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FirstServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastPaymentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimBalances", x => x.ClaimId);
                });

            migrationBuilder.CreateTable(
                name: "ExpectedClaims",
                schema: "remittance",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TotalCharge = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    LastStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FirstServiceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpectedClaims", x => x.ClaimId);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "remittance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Topic = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TraceParent = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Postings",
                schema: "remittance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemittanceFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClpStatus = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    PayerClaimId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Charge = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Paid = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Allowed = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PatientResponsibility = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    ContractualAdjustments = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    OtherAdjustments = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    DenialCarc = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    DenialCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    RemarkCodes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EftTraceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Postings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedEvents",
                schema: "remittance",
                columns: table => new
                {
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Consumer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedEvents", x => new { x.EventId, x.Consumer });
                });

            migrationBuilder.CreateTable(
                name: "ReasonCodes",
                schema: "remittance",
                columns: table => new
                {
                    Kind = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Explanation = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReasonCodes", x => new { x.Kind, x.Code });
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationQueue",
                schema: "remittance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RemittanceFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ClaimNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PayerClaimId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Charge = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Paid = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    RawClpJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationQueue", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RemittanceFiles",
                schema: "remittance",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BlobContainer = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    BlobName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    EftTraceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: true),
                    TotalPaid = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ClaimCount = table.Column<int>(type: "int", nullable: false),
                    MatchedCount = table.Column<int>(type: "int", nullable: false),
                    UnmatchedCount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PdfBlobName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RemittanceFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostingAdjustments",
                schema: "remittance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PostingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GroupCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostingAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostingAdjustments_Postings_PostingId",
                        column: x => x.PostingId,
                        principalSchema: "remittance",
                        principalTable: "Postings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimBalances_OrgId_Status",
                schema: "remittance",
                table: "ClaimBalances",
                columns: new[] { "OrgId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpectedClaims_ClaimNumber",
                schema: "remittance",
                table: "ExpectedClaims",
                column: "ClaimNumber");

            migrationBuilder.CreateIndex(
                name: "IX_ExpectedClaims_PayerId_ClaimNumber",
                schema: "remittance",
                table: "ExpectedClaims",
                columns: new[] { "PayerId", "ClaimNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_ProcessedAt_OccurredAt",
                schema: "remittance",
                table: "Outbox",
                columns: new[] { "ProcessedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostingAdjustments_PostingId",
                schema: "remittance",
                table: "PostingAdjustments",
                column: "PostingId");

            migrationBuilder.CreateIndex(
                name: "IX_Postings_ClaimId",
                schema: "remittance",
                table: "Postings",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_Postings_OrgId_PostedAt",
                schema: "remittance",
                table: "Postings",
                columns: new[] { "OrgId", "PostedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Postings_RemittanceFileId",
                schema: "remittance",
                table: "Postings",
                column: "RemittanceFileId");

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationQueue_ResolvedAt",
                schema: "remittance",
                table: "ReconciliationQueue",
                column: "ResolvedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RemittanceFiles_ReceivedAt",
                schema: "remittance",
                table: "RemittanceFiles",
                column: "ReceivedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "remittance");

            migrationBuilder.DropTable(
                name: "ClaimBalances",
                schema: "remittance");

            migrationBuilder.DropTable(
                name: "ExpectedClaims",
                schema: "remittance");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "remittance");

            migrationBuilder.DropTable(
                name: "PostingAdjustments",
                schema: "remittance");

            migrationBuilder.DropTable(
                name: "ProcessedEvents",
                schema: "remittance");

            migrationBuilder.DropTable(
                name: "ReasonCodes",
                schema: "remittance");

            migrationBuilder.DropTable(
                name: "ReconciliationQueue",
                schema: "remittance");

            migrationBuilder.DropTable(
                name: "RemittanceFiles",
                schema: "remittance");

            migrationBuilder.DropTable(
                name: "Postings",
                schema: "remittance");
        }
    }
}
