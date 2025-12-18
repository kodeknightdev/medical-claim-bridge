using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClaimBridge.Claims.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "claims");

            migrationBuilder.CreateSequence(
                name: "ClaimNumbers",
                schema: "claims",
                startValue: 100000L);

            migrationBuilder.CreateTable(
                name: "AuditLog",
                schema: "claims",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Target = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ClaimLifecycles",
                schema: "claims",
                columns: table => new
                {
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeadlineKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DeadlineAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    InquiriesSent = table.Column<int>(type: "int", nullable: false),
                    Resubmissions = table.Column<int>(type: "int", nullable: false),
                    EscalatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimLifecycles", x => x.ClaimId);
                });

            migrationBuilder.CreateTable(
                name: "Claims",
                schema: "claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClaimNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrgId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    PreviousClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SupersededByClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FrequencyCode = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    OriginalClaimNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PayerId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BillingNpi = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    RenderingNpi = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    MemberIdProtected = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MemberIdHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    MemberIdLast4 = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    PatientFirstNameProtected = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PatientLastNameProtected = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PatientDobProtected = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PatientGender = table.Column<string>(type: "nvarchar(1)", maxLength: 1, nullable: false),
                    PatientBirthYear = table.Column<int>(type: "int", nullable: false),
                    PriorAuthNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Diagnoses = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    StatusChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SubmittedBy = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TotalCharge = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: false),
                    PaidAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    AllowedAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    PatientResponsibility = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    AdjustmentAmount = table.Column<decimal>(type: "decimal(12,2)", precision: 12, scale: 2, nullable: true),
                    DenialCategory = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DenialCarc = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    DenialRisk = table.Column<decimal>(type: "decimal(5,4)", precision: 5, scale: 4, nullable: true),
                    DenialRiskModel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DenialRiskFactorsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Fingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    PossibleDuplicateOf = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    ScrubAttempts = table.Column<int>(type: "int", nullable: false),
                    PayerClaimId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    FirstSubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Claims", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                schema: "claims",
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
                name: "ProcessedEvents",
                schema: "claims",
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
                name: "ClaimStatusEvents",
                schema: "claims",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ClaimId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DetailJson = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimStatusEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClaimStatusEvents_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalSchema: "claims",
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_AuditLog_At",
                schema: "claims",
                table: "AuditLog",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimLifecycles_DeadlineAt",
                schema: "claims",
                table: "ClaimLifecycles",
                column: "DeadlineAt");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ClaimNumber",
                schema: "claims",
                table: "Claims",
                column: "ClaimNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Claims_CreatedAt",
                schema: "claims",
                table: "Claims",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_MemberIdHash",
                schema: "claims",
                table: "Claims",
                column: "MemberIdHash");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_OrgId_Fingerprint",
                schema: "claims",
                table: "Claims",
                columns: new[] { "OrgId", "Fingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_Claims_OrgId_Status",
                schema: "claims",
                table: "Claims",
                columns: new[] { "OrgId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimStatusEvents_ClaimId_Sequence",
                schema: "claims",
                table: "ClaimStatusEvents",
                columns: new[] { "ClaimId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_ProcessedAt_OccurredAt",
                schema: "claims",
                table: "Outbox",
                columns: new[] { "ProcessedAt", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLog",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "ClaimLifecycles",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "ClaimStatusEvents",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "Outbox",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "ProcessedEvents",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "ServiceLines",
                schema: "claims");

            migrationBuilder.DropTable(
                name: "Claims",
                schema: "claims");

            migrationBuilder.DropSequence(
                name: "ClaimNumbers",
                schema: "claims");
        }
    }
}
