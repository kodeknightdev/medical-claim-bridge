using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimBridge.Providers.Data;

public class ProvidersDb(DbContextOptions<ProvidersDb> options) : DbContext(options), IMessagingDbContext
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<ProviderOrganization> Organizations => Set<ProviderOrganization>();
    public DbSet<RenderingProvider> RenderingProviders => Set<RenderingProvider>();
    public DbSet<Payer> Payers => Set<Payer>();
    public DbSet<PayerEnrollment> Enrollments => Set<PayerEnrollment>();
    public DbSet<WebhookSubscription> Webhooks => Set<WebhookSubscription>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("providers");
        b.AddMessagingTables();

        b.Entity<ProviderOrganization>(e =>
        {
            e.ToTable("Organizations");
            e.Property(x => x.Name).HasMaxLength(150);
            e.Property(x => x.GroupNpi).HasMaxLength(10).IsFixedLength();
            e.Property(x => x.TaxId).HasMaxLength(10);
            e.Property(x => x.Taxonomy).HasMaxLength(10);
            e.Property(x => x.AddressLine1).HasMaxLength(120);
            e.Property(x => x.City).HasMaxLength(60);
            e.Property(x => x.State).HasMaxLength(2);
            e.Property(x => x.Zip).HasMaxLength(10);
            e.Property(x => x.ContactEmail).HasMaxLength(150);
            e.HasIndex(x => x.GroupNpi).IsUnique();
            e.HasMany(x => x.Providers).WithOne().HasForeignKey(p => p.OrgId);
        });

        b.Entity<RenderingProvider>(e =>
        {
            e.Property(x => x.Npi).HasMaxLength(10).IsFixedLength();
            e.Property(x => x.FirstName).HasMaxLength(60);
            e.Property(x => x.LastName).HasMaxLength(60);
            e.Property(x => x.Taxonomy).HasMaxLength(10);
            e.HasIndex(x => x.Npi).IsUnique();
        });

        b.Entity<Payer>(e =>
        {
            e.Property(x => x.Id).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Channel).HasMaxLength(20);
        });

        b.Entity<PayerEnrollment>(e =>
        {
            e.ToTable("Enrollments");
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(12);
            e.Property(x => x.RequestedBy).HasMaxLength(150);
            e.Property(x => x.DecidedBy).HasMaxLength(150);
            e.Property(x => x.Note).HasMaxLength(500);
            e.HasIndex(x => new { x.OrgId, x.PayerId }).IsUnique();
        });

        b.Entity<WebhookSubscription>(e =>
        {
            e.ToTable("WebhookSubscriptions");
            e.Property(x => x.Url).HasMaxLength(400);
            e.Property(x => x.Events).HasMaxLength(300);
            e.Property(x => x.SigningSecretProtected).HasMaxLength(200);
            e.Property(x => x.DisabledReason).HasMaxLength(300);
            e.Ignore(x => x.EventList);
            e.HasIndex(x => x.OrgId);
        });

        b.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.Property(x => x.Actor).HasMaxLength(150);
            e.Property(x => x.Action).HasMaxLength(80);
            e.Property(x => x.Target).HasMaxLength(150);
        });
    }
}

public class DesignTimeFactory : IDesignTimeDbContextFactory<ProvidersDb>
{
    public ProvidersDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ProvidersDb>().UseSqlServer("Server=localhost;Database=cb_providers").Options);
}
