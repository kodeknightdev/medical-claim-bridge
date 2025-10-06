using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimBridge.Remittance.Data;

public class RemittanceDb(DbContextOptions<RemittanceDb> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<ExpectedClaim> ExpectedClaims => Set<ExpectedClaim>();
    public DbSet<RemittanceFile> Files => Set<RemittanceFile>();
    public DbSet<Posting> Postings => Set<Posting>();
    public DbSet<UnmatchedLine> Unmatched => Set<UnmatchedLine>();
    public DbSet<ClaimBalance> Balances => Set<ClaimBalance>();
    public DbSet<ReasonCode> ReasonCodes => Set<ReasonCode>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("remittance");
        b.AddMessagingTables();

        b.Entity<ExpectedClaim>(e =>
        {
            e.HasKey(x => x.ClaimId);
            e.Property(x => x.ClaimNumber).HasMaxLength(20);
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.LastStatus).HasMaxLength(20);
            e.Property(x => x.TotalCharge).HasPrecision(12, 2);
            e.HasIndex(x => new { x.PayerId, x.ClaimNumber });
            e.HasIndex(x => x.ClaimNumber);
        });

        b.Entity<RemittanceFile>(e =>
        {
            e.ToTable("RemittanceFiles");
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.BlobContainer).HasMaxLength(60);
            e.Property(x => x.BlobName).HasMaxLength(250);
            e.Property(x => x.EftTraceNumber).HasMaxLength(50);
            e.Property(x => x.TotalPaid).HasPrecision(14, 2);
            e.Property(x => x.PaymentMethod).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(500);
            e.Property(x => x.PdfBlobName).HasMaxLength(250);
            e.HasIndex(x => x.ReceivedAt);
        });

        b.Entity<Posting>(e =>
        {
            e.Property(x => x.ClaimNumber).HasMaxLength(20);
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.ClpStatus).HasMaxLength(3);
            e.Property(x => x.PayerClaimId).HasMaxLength(40);
            e.Property(x => x.DenialCarc).HasMaxLength(5);
            e.Property(x => x.DenialCategory).HasMaxLength(30);
            e.Property(x => x.EftTraceNumber).HasMaxLength(50);
            foreach (var p in new[] { nameof(Posting.Charge), nameof(Posting.Paid), nameof(Posting.Allowed), nameof(Posting.PatientResponsibility),
                         nameof(Posting.ContractualAdjustments), nameof(Posting.OtherAdjustments) })
                e.Property(p).HasPrecision(12, 2);
            e.HasMany(x => x.Adjustments).WithOne().HasForeignKey(a => a.PostingId);
            e.HasIndex(x => x.ClaimId);
            e.HasIndex(x => new { x.OrgId, x.PostedAt });
            e.HasIndex(x => x.RemittanceFileId);
        });

        b.Entity<PostingAdjustment>(e =>
        {
            e.ToTable("PostingAdjustments");
            e.Property(x => x.GroupCode).HasMaxLength(2);
            e.Property(x => x.ReasonCode).HasMaxLength(5);
            e.Property(x => x.Amount).HasPrecision(12, 2);
        });

        b.Entity<UnmatchedLine>(e =>
        {
            e.ToTable("ReconciliationQueue");
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.ClaimNumber).HasMaxLength(30);
            e.Property(x => x.PayerClaimId).HasMaxLength(40);
            e.Property(x => x.Charge).HasPrecision(12, 2);
            e.Property(x => x.Paid).HasPrecision(12, 2);
            e.Property(x => x.Reason).HasMaxLength(300);
            e.Property(x => x.ResolvedBy).HasMaxLength(150);
            e.Property(x => x.Resolution).HasMaxLength(300);
            e.HasIndex(x => x.ResolvedAt);
        });

        b.Entity<ClaimBalance>(e =>
        {
            e.ToTable("ClaimBalances");
            e.HasKey(x => x.ClaimId);
            e.Property(x => x.ClaimNumber).HasMaxLength(20);
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(10);
            foreach (var p in new[] { nameof(ClaimBalance.Charge), nameof(ClaimBalance.Paid), nameof(ClaimBalance.Adjusted),
                         nameof(ClaimBalance.PatientResponsibility), nameof(ClaimBalance.PayerOutstanding) })
                e.Property(p).HasPrecision(12, 2);
            e.HasIndex(x => new { x.OrgId, x.Status });
        });

        b.Entity<ReasonCode>(e =>
        {
            e.ToTable("ReasonCodes");
            e.HasKey(x => new { x.Kind, x.Code });
            e.Property(x => x.Kind).HasMaxLength(5);
            e.Property(x => x.Code).HasMaxLength(6);
            e.Property(x => x.Category).HasMaxLength(30);
            e.Property(x => x.Explanation).HasMaxLength(300);
        });

        b.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.Property(x => x.Actor).HasMaxLength(150);
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.Target).HasMaxLength(100);
            e.Property(x => x.Detail).HasMaxLength(500);
        });
    }
}

public class DesignTimeFactory : IDesignTimeDbContextFactory<RemittanceDb>
{
    public RemittanceDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<RemittanceDb>().UseSqlServer("Server=localhost;Database=cb_remittance").Options);
}
