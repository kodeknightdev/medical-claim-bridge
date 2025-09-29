using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimBridge.PayersGateway.Data;

public class PayersDb(DbContextOptions<PayersDb> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<PayerStatusEntry> StatusEntries => Set<PayerStatusEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("payers");
        b.AddMessagingTables();

        b.Entity<Submission>(e =>
        {
            e.Property(x => x.ClaimNumber).HasMaxLength(20);
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.Channel).HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.PayerClaimId).HasMaxLength(40);
            e.Property(x => x.FileName).HasMaxLength(120);
            e.Property(x => x.InterchangeControlNumber).HasMaxLength(9);
            e.Property(x => x.RejectReasons).HasMaxLength(1000);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.Property(x => x.LastPayerStatus).HasMaxLength(20);
            e.HasIndex(x => x.ClaimId);
            e.HasIndex(x => new { x.PayerId, x.Status });
            e.HasIndex(x => x.ClaimNumber);
        });

        b.Entity<PayerStatusEntry>(e =>
        {
            e.ToTable("StatusHistory");
            e.Property(x => x.ClaimNumber).HasMaxLength(20);
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.HasIndex(x => new { x.ClaimId, x.At });
        });
    }
}

public class DesignTimeFactory : IDesignTimeDbContextFactory<PayersDb>
{
    public PayersDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PayersDb>().UseSqlServer("Server=localhost;Database=cb_payers").Options);
}
