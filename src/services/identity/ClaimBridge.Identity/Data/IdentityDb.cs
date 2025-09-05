using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Identity.Data;

public class IdentityDb(DbContextOptions<IdentityDb> options) : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();

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
