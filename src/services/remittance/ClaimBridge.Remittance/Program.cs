using ClaimBridge.Contracts.Events;
using ClaimBridge.Remittance.Consumers;
using ClaimBridge.Remittance.Data;
using ClaimBridge.Remittance.Domain;
using ClaimBridge.Remittance.Jobs;
using ClaimBridge.ServiceDefaults;
using ClaimBridge.ServiceDefaults.Auth;
using ClaimBridge.ServiceDefaults.Data;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.ServiceDefaults.Storage;
using Microsoft.EntityFrameworkCore;
using Quartz;

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("remittance");
builder.AddClaimBridgeAuth();

var config = builder.Configuration;
builder.Services.AddDbContext<RemittanceDb>(o => o.UseSqlServer(config.GetConnectionString("RemittanceDb")));
builder.Services.AddClaimBridgeBlobStorage(config);
builder.Services.AddClaimBridgeMessaging<RemittanceDb>(config, "remittance")
    .Consume<RemittanceReceivedHandler>("remittance-era", Topics.RemittanceReceived)
    .Consume<ClaimStatusProjection>("remittance-claims", Topics.ClaimStatusChanged);

builder.Services.AddScoped<PostingService>();
builder.Services.AddSingleton<RemittanceMetrics>();
builder.Services.AddScoped<ArSnapshotService>();
builder.Services.AddQuartz(q =>
{
    var retry = new JobKey("reconciliation-retry");
    q.AddJob<ReconciliationRetryJob>(retry);
    q.AddTrigger(t => t.ForJob(retry).StartNow().WithSimpleSchedule(s => s.WithIntervalInMinutes(2).RepeatForever()));

    var snapshot = new JobKey("ar-snapshot");
    q.AddJob<ArSnapshotJob>(snapshot);
    q.AddTrigger(t => t.ForJob(snapshot).WithCronSchedule(config["Jobs:ArSnapshotCron"] ?? "0 0 2 * * ?"));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

builder.Services.AddHealthChecks().AddSqlServerReadiness(config, "RemittanceDb");
builder.Services.AddControllers();

var app = builder.Build();

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    await app.MigrateAsync<RemittanceDb>((_, db) => ReasonCodeSeeder.SeedAsync(db));

app.Run();

public partial class Program;
