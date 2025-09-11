using ClaimBridge.Providers.Controllers;
using ClaimBridge.Providers.Data;
using ClaimBridge.Providers.Seeding;
using ClaimBridge.ServiceDefaults;
using ClaimBridge.ServiceDefaults.Auth;
using ClaimBridge.ServiceDefaults.Data;
using ClaimBridge.ServiceDefaults.Phi;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("providers");
builder.AddClaimBridgeAuth();

builder.Services.AddDbContext<ProvidersDb>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("ProvidersDb")));
builder.Services.AddPhiProtection(builder.Configuration);
builder.Services.AddScoped<EnrollmentService>();
builder.Services.AddHealthChecks().AddSqlServerReadiness(builder.Configuration, "ProvidersDb");
builder.Services.AddControllers();

var app = builder.Build();

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    await app.MigrateAsync<ProvidersDb>((sp, db) => ProvidersSeeder.SeedAsync(db, TimeProvider.System));

app.Run();

public partial class Program;
