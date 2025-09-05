using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimBridge.Identity.Data;

public class DesignTimeFactory : IDesignTimeDbContextFactory<IdentityDb>
{
    public IdentityDb CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityDb>()
            .UseSqlServer("Server=localhost;Database=cb_identity;Trusted_Connection=False")
            .Options;
        return new IdentityDb(options);
    }
}
