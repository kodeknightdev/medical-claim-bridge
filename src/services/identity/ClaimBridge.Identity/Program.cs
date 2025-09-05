using ClaimBridge.Identity.Data;
using ClaimBridge.Identity.Seeding;
using ClaimBridge.ServiceDefaults;
using ClaimBridge.ServiceDefaults.Data;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("identity");

builder.Services.AddDbContext<IdentityDb>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("IdentityDb")));

builder.Services
    .AddIdentity<AppUser, AppRole>(o =>
    {
        o.User.RequireUniqueEmail = true;
        o.Password.RequiredLength = 8;
        o.Lockout.MaxFailedAccessAttempts = 5;
    })
    .AddEntityFrameworkStores<IdentityDb>()
    .AddDefaultTokenProviders();

builder.Services.AddHealthChecks().AddSqlServerReadiness(builder.Configuration, "IdentityDb");
builder.Services.AddControllers();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.All;
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    await app.MigrateAsync<IdentityDb>((sp, _) =>
        IdentitySeeder.SeedAsync(sp, app.Configuration, app.Logger));

app.Run();

public partial class Program;
