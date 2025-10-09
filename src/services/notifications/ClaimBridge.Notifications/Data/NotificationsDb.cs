using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimBridge.Notifications.Data;

public class NotificationsDb(DbContextOptions<NotificationsDb> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<WebhookDelivery> Deliveries => Set<WebhookDelivery>();
    public DbSet<DeliveryAttempt> Attempts => Set<DeliveryAttempt>();
    public DbSet<SubscriptionHealth> SubscriptionHealth => Set<SubscriptionHealth>();
    public DbSet<EmailLog> Emails => Set<EmailLog>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("notifications");
        b.AddMessagingTables();

        b.Entity<WebhookDelivery>(e =>
        {
            e.ToTable("WebhookDeliveries");
            e.Property(x => x.EventType).HasMaxLength(40);
            e.Property(x => x.Url).HasMaxLength(400);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.LastError).HasMaxLength(500);
            e.HasMany(x => x.AttemptLog).WithOne().HasForeignKey(a => a.DeliveryId);
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
            e.HasIndex(x => new { x.SubscriptionId, x.CreatedAt });
            e.HasIndex(x => new { x.SourceEventId, x.SubscriptionId }).IsUnique();
        });
        b.Entity<DeliveryAttempt>(e =>
        {
            e.ToTable("DeliveryAttempts");
            e.Property(x => x.Error).HasMaxLength(500);
        });
        b.Entity<SubscriptionHealth>(e => e.HasKey(x => x.SubscriptionId));
        b.Entity<EmailLog>(e =>
        {
            e.ToTable("EmailLog");
            e.Property(x => x.To).HasMaxLength(150);
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Template).HasMaxLength(40);
            e.Property(x => x.Error).HasMaxLength(300);
        });
        b.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.Property(x => x.Actor).HasMaxLength(150);
            e.Property(x => x.Action).HasMaxLength(60);
            e.Property(x => x.Target).HasMaxLength(100);
        });
    }
}

public class DesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDb>
{
    public NotificationsDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationsDb>().UseSqlServer("Server=localhost;Database=cb_notifications").Options);
}
