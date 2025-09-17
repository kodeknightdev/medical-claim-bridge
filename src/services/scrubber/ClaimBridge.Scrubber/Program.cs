using ClaimBridge.Scrubber.CodeSetsCache;
using ClaimBridge.Scrubber.Data;
using ClaimBridge.Scrubber.Rules;
using ClaimBridge.Scrubber.Rules.PayerRules;
using ClaimBridge.ServiceDefaults;
using ClaimBridge.ServiceDefaults.Auth;
using ClaimBridge.ServiceDefaults.Caching;
using ClaimBridge.ServiceDefaults.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("scrubber");
builder.AddClaimBridgeAuth();

builder.Services.AddDbContext<ScrubberDb>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("ScrubberDb")));
builder.Services.AddClaimBridgeCaching(builder.Configuration);
builder.Services.AddScoped<CodeSetProvider>();
builder.Services.AddBuiltInScrubRules();
builder.Services.AddScoped<ScrubService>();
builder.Services.AddScoped<PayerRuleProvider>();
builder.Services.AddSingleton<ScrubberMetrics>();
builder.Services.AddHealthChecks().AddSqlServerReadiness(builder.Configuration, "ScrubberDb");
builder.Services.AddControllers();

var app = builder.Build();

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    await app.MigrateAsync<ScrubberDb>(async (_, db) =>
    {
        await CodeSetSeeder.SeedAsync(db);
        await PayerRuleSeeder.SeedAsync(db);
    });

app.Run();

public partial class Program;
