using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Application.Clients;
using ClaimBridge.Claims.Data;
using ClaimBridge.Scrubber.Grpc;
using ClaimBridge.ServiceDefaults;
using ClaimBridge.ServiceDefaults.Auth;
using ClaimBridge.ServiceDefaults.Caching;
using ClaimBridge.ServiceDefaults.Data;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.ServiceDefaults.Phi;
using ClaimBridge.ServiceDefaults.Validation;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("claims");
builder.AddClaimBridgeAuth();

var config = builder.Configuration;
builder.Services.AddDbContext<ClaimsDb>(o => o.UseSqlServer(config.GetConnectionString("ClaimsDb")));
builder.Services.AddPhiProtection(config);
builder.Services.AddClaimBridgeCaching(config);
builder.Services.AddClaimBridgeMessaging<ClaimsDb>(config, "claims");

builder.Services.AddGrpcClient<ClaimScrubber.ClaimScrubberClient>(o => o.Address = new Uri(config["Services:ScrubberGrpc"]!));
builder.Services.AddHttpClient<ProvidersClient>(c => c.BaseAddress = new Uri(config["Services:Providers"]!))
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient<EligibilityClient>(c => c.BaseAddress = new Uri(config["Services:PayersGateway"]!))
    .AddStandardResilienceHandler(o => o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(8));
builder.Services.AddHttpClient<RiskClient>(c =>
{
    c.BaseAddress = new Uri(config["Services:DenialPredictor"]!);
    c.Timeout = TimeSpan.FromSeconds(2);
});

builder.Services.AddScoped<ScrubberClient>();
builder.Services.AddScoped<ClaimPhi>();
builder.Services.AddScoped<ClaimTransitions>();
builder.Services.AddScoped<ClaimIntakeService>();
builder.Services.AddScoped<DuplicateDetector>();
builder.Services.AddScoped<ClaimBridge.Claims.Api.IdempotencyFilter>();
builder.Services.AddScoped<ClaimReadModelProjector>();
builder.Services.AddValidatorsFromAssemblyContaining<ClaimRequestValidator>();

builder.Services.AddHealthChecks().AddSqlServerReadiness(config, "ClaimsDb");
builder.Services.AddControllers(o => o.Filters.Add<FluentValidationFilter>());

var app = builder.Build();

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    await app.MigrateAsync<ClaimsDb>();

app.Run();

public partial class Program;
