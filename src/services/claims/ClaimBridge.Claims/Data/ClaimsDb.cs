using ClaimBridge.Claims.Domain;
using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimBridge.Claims.Data;

public class ClaimsDb(DbContextOptions<ClaimsDb> options) : DbContext(options), IMessagingDbContext
{
    public const string Schema = "claims";

    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<ClaimStatusEvent> StatusEvents => Set<ClaimStatusEvent>();
    public DbSet<ClaimLifecycleState> Lifecycles => Set<ClaimLifecycleState>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<ClaimSearchEntry> SearchEntries => Set<ClaimSearchEntry>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema(Schema);
        b.HasSequence<long>("ClaimNumbers").StartsAt(100000).IncrementsBy(1);
        b.AddMessagingTables();

        b.Entity<Claim>(e =>
        {
            e.Property(x => x.ClaimNumber).HasMaxLength(20);
            e.HasIndex(x => x.ClaimNumber).IsUnique();
            e.Property(x => x.FrequencyCode).HasMaxLength(1);
            e.Property(x => x.OriginalClaimNumber).HasMaxLength(20);
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.BillingNpi).HasMaxLength(10);
            e.Property(x => x.RenderingNpi).HasMaxLength(10);
            e.Property(x => x.MemberIdProtected).HasMaxLength(200);
            e.Property(x => x.MemberIdHash).HasMaxLength(64);
            e.Property(x => x.MemberIdLast4).HasMaxLength(4);
            e.Property(x => x.PatientFirstNameProtected).HasMaxLength(200);
            e.Property(x => x.PatientLastNameProtected).HasMaxLength(200);
            e.Property(x => x.PatientDobProtected).HasMaxLength(200);
            e.Property(x => x.PatientGender).HasMaxLength(1);
            e.Property(x => x.PriorAuthNumber).HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.SubmittedBy).HasMaxLength(150);
            e.Property(x => x.TotalCharge).HasPrecision(12, 2);
            e.Property(x => x.PaidAmount).HasPrecision(12, 2);
            e.Property(x => x.AllowedAmount).HasPrecision(12, 2);
            e.Property(x => x.PatientResponsibility).HasPrecision(12, 2);
            e.Property(x => x.AdjustmentAmount).HasPrecision(12, 2);
            e.Property(x => x.DenialCategory).HasMaxLength(30);
            e.Property(x => x.DenialCarc).HasMaxLength(5);
            e.Property(x => x.DenialRisk).HasPrecision(5, 4);
            e.Property(x => x.DenialRiskModel).HasMaxLength(40);
            e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.PossibleDuplicateOf).HasMaxLength(20);
            e.Property(x => x.PayerClaimId).HasMaxLength(40);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Ignore(x => x.FirstServiceDate);

            e.HasIndex(x => new { x.OrgId, x.Status });
            e.HasIndex(x => new { x.OrgId, x.Fingerprint });
            e.HasIndex(x => x.MemberIdHash);
            e.HasIndex(x => x.CreatedAt);

            e.OwnsMany(x => x.Lines, l =>
            {
                l.ToTable("ServiceLines");
                l.WithOwner().HasForeignKey("ClaimId");
                l.HasKey("ClaimId", nameof(ServiceLine.LineNumber));
                l.Property(x => x.ProcedureCode).HasMaxLength(5);
                l.Property(x => x.PlaceOfService).HasMaxLength(2);
                l.Property(x => x.Units).HasPrecision(7, 2);
                l.Property(x => x.Charge).HasPrecision(12, 2);
            });

            e.HasMany(x => x.History).WithOne().HasForeignKey(h => h.ClaimId);
        });

        b.Entity<ClaimStatusEvent>(e =>
        {
            e.ToTable("ClaimStatusEvents");
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Trigger).HasMaxLength(40);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Source).HasMaxLength(40);
            e.HasIndex(x => new { x.ClaimId, x.Sequence }).IsUnique();
        });

        b.Entity<ClaimLifecycleState>(e =>
        {
            e.ToTable("ClaimLifecycles");
            e.HasKey(x => x.ClaimId);
            e.Property(x => x.DeadlineKind).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.DeadlineAt);
        });

        b.Entity<ClaimSearchEntry>(e =>
        {
            e.ToTable("ClaimSearch");
            e.HasKey(x => x.ClaimId);
            e.Property(x => x.ClaimNumber).HasMaxLength(20);
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.FrequencyCode).HasMaxLength(1);
            e.Property(x => x.MemberIdLast4).HasMaxLength(4);
            e.Property(x => x.MemberIdHash).HasMaxLength(64);
            e.Property(x => x.ProcedureCodes).HasMaxLength(300);
            e.Property(x => x.TotalCharge).HasPrecision(12, 2);
            e.Property(x => x.PaidAmount).HasPrecision(12, 2);
            e.Property(x => x.Balance).HasPrecision(12, 2);
            e.Property(x => x.DenialRisk).HasPrecision(5, 4);
            e.Property(x => x.DenialCategory).HasMaxLength(30);
            e.Property(x => x.Source).HasMaxLength(10);
            e.HasIndex(x => new { x.OrgId, x.Status, x.CreatedAt });
            e.HasIndex(x => x.ClaimNumber);
            e.HasIndex(x => x.MemberIdHash);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.ToTable("IdempotencyKeys");
            e.Property(x => x.ClientId).HasMaxLength(100);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.HasIndex(x => new { x.ClientId, x.Key }).IsUnique();
            e.HasIndex(x => x.CreatedAt);
        });

        b.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.Property(x => x.Actor).HasMaxLength(150);
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.Target).HasMaxLength(100);
            e.Property(x => x.Detail).HasMaxLength(500);
            e.HasIndex(x => x.At);
        });
    }

    public async Task<string> NextClaimNumberAsync(DateTimeOffset now, CancellationToken ct)
    {
        var seq = await Database.SqlQueryRaw<long>($"SELECT NEXT VALUE FOR [{Schema}].[ClaimNumbers] AS [Value]").SingleAsync(ct);
        return $"CB{now:yy}{seq:D7}";
    }
}

public class DesignTimeFactory : IDesignTimeDbContextFactory<ClaimsDb>
{
    public ClaimsDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ClaimsDb>().UseSqlServer("Server=localhost;Database=cb_claims").Options);
}
