using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimBridge.Scrubber.Data;

public class ScrubberDb(DbContextOptions<ScrubberDb> options) : DbContext(options)
{
    public DbSet<IcdCode> IcdCodes => Set<IcdCode>();
    public DbSet<ProcedureCode> ProcedureCodes => Set<ProcedureCode>();
    public DbSet<Modifier> Modifiers => Set<Modifier>();
    public DbSet<MedicalNecessityEntry> MedicalNecessity => Set<MedicalNecessityEntry>();
    public DbSet<PayerSetting> PayerSettings => Set<PayerSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("scrubber");

        b.Entity<IcdCode>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(8);
            e.Property(x => x.Description).HasMaxLength(250);
        });
        b.Entity<ProcedureCode>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(5);
            e.Property(x => x.Description).HasMaxLength(120);
            e.Property(x => x.Category).HasMaxLength(20);
            e.Property(x => x.TypicalCharge).HasPrecision(10, 2);
        });
        b.Entity<Modifier>(e =>
        {
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(2);
            e.Property(x => x.Description).HasMaxLength(120);
            e.Property(x => x.AllowedCategories).HasMaxLength(120);
        });
        b.Entity<MedicalNecessityEntry>(e =>
        {
            e.ToTable("MedicalNecessity");
            e.Property(x => x.ProcedureCode).HasMaxLength(5);
            e.Property(x => x.DxPrefix).HasMaxLength(8);
            e.HasIndex(x => new { x.ProcedureCode, x.DxPrefix }).IsUnique();
        });
        b.Entity<PayerSetting>(e =>
        {
            e.HasKey(x => x.PayerId);
            e.Property(x => x.PayerId).HasMaxLength(20);
            e.Property(x => x.Name).HasMaxLength(100);
        });
    }
}

public class DesignTimeFactory : IDesignTimeDbContextFactory<ScrubberDb>
{
    public ScrubberDb CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<ScrubberDb>().UseSqlServer("Server=localhost;Database=cb_scrubber").Options);
}
