using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimBridge.PayersGateway.Data;

public class PayersDb(DbContextOptions<PayersDb> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<Submission> Submissions => Set<Submission>();
    public DbSet<PayerStatusEntry> StatusEntries => Set<PayerStatusEntry>();
    public DbSet<PayerFile> Files => Set<PayerFile>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("payers");
        b.AddMessagingTables();
        b.HasSequence<int>("InterchangeControlNumbers").StartsAt(1000);

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

        b.Entity<PayerFile>(e =>
        {
            e.ToTable("Files");
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Kind).HasMaxLength(5);
            e.Property(x => x.FileName).HasMaxLength(120);
            e.Property(x => x.BlobContainer).HasMaxLength(60);
            e.Property(x => x.BlobName).HasMaxLength(250);
            e.Property(x => x.ControlNumber).HasMaxLength(9);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasIndex(x => new { x.PayerId, x.Kind, x.ControlNumber });
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

public static class PayersDbExtensions
{
    public static async Task<int> NextInterchangeControlNumberAsync(this PayersDb db, CancellationToken ct) =>
        await db.Database.SqlQueryRaw<int>("SELECT NEXT VALUE FOR [payers].[InterchangeControlNumbers] AS [Value]").SingleAsync(ct);
}

public class DesignTimeFactory : IDesignTimeDbContextFactory<PayersDb>
{
    public PayersDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<PayersDb>().UseSqlServer("Server=localhost;Database=cb_payers").Options);
}
