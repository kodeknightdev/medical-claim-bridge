using ClaimBridge.Contracts.Reference;
using ClaimBridge.Identity.Data;
using Microsoft.AspNetCore.Identity;

namespace ClaimBridge.Identity.Seeding;

public static class IdentitySeeder
{
    private sealed record DemoUser(string Email, string DisplayName, string Role, Guid? OrgId);

    private static readonly DemoUser[] DemoUsers =
    [
        new("admin@claimbridge.local", "Clearinghouse Admin", Roles.Admin, null),
        new("ops@claimbridge.local", "Ops Analyst", Roles.OpsAnalyst, null),
        new("admin@sunriseclinic.local", "Sunrise Practice Admin", Roles.ProviderAdmin, SeedIds.SunriseOrg),
        new("billing@sunriseclinic.local", "Sunrise Billing", Roles.ProviderBiller, SeedIds.SunriseOrg),
        new("billing@lakesideortho.local", "Lakeside Billing", Roles.ProviderBiller, SeedIds.LakesideOrg),
    ];

    public static async Task SeedAsync(IServiceProvider services, IConfiguration config, ILogger logger)
    {
        var roles = services.GetRequiredService<RoleManager<AppRole>>();
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new AppRole(role));

        var password = config["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed:DemoPassword not set, skipping demo users");
            return;
        }

        var users = services.GetRequiredService<UserManager<AppUser>>();
        foreach (var demo in DemoUsers)
        {
            if (await users.FindByEmailAsync(demo.Email) is not null) continue;
            var user = new AppUser
            {
                UserName = demo.Email,
                Email = demo.Email,
                EmailConfirmed = true,
                DisplayName = demo.DisplayName,
                OrgId = demo.OrgId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException($"Seeding {demo.Email} failed: {string.Join("; ", result.Errors.Select(e => e.Description))}");
            await users.AddToRoleAsync(user, demo.Role);
        }
    }
}
