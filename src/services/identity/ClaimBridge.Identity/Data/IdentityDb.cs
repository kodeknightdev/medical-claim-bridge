using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Identity.Data;

public class IdentityDb(DbContextOptions<IdentityDb> options) : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<ApiClientProfile> ApiClients => Set<ApiClientProfile>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("identity");
        builder.UseOpenIddict();

        builder.Entity<AppUser>(e =>
        {
            e.Property(u => u.DisplayName).HasMaxLength(120);
            e.HasIndex(u => u.OrgId);
        });

        builder.Entity<ApiClientProfile>(e =>
        {
            e.ToTable("ApiClients");
            e.HasKey(c => c.ClientId);
            e.Property(c => c.ClientId).HasMaxLength(100);
            e.Property(c => c.DisplayName).HasMaxLength(120);
            e.Property(c => c.Scopes).HasMaxLength(200);
            e.Property(c => c.CreatedBy).HasMaxLength(200);
            e.Ignore(c => c.ScopeList);
            e.HasIndex(c => c.OrgId);
        });

        builder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.Property(a => a.Actor).HasMaxLength(200);
            e.Property(a => a.Action).HasMaxLength(80);
            e.Property(a => a.Target).HasMaxLength(200);
            e.HasIndex(a => a.At);
        });
    }
}
