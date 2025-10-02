using ClaimBridge.Contracts.Events;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.PayersGateway.Adapters.PayerA;
using ClaimBridge.PayersGateway.Adapters.PayerB;
using ClaimBridge.PayersGateway.Adapters.PayerC;
using ClaimBridge.PayersGateway.Api;
using ClaimBridge.PayersGateway.Consumers;
using ClaimBridge.PayersGateway.Data;
using ClaimBridge.PayersGateway.Files;
using ClaimBridge.PayersGateway.Jobs;
using ClaimBridge.PayersGateway.Resilience;
using ClaimBridge.ServiceDefaults;
using ClaimBridge.ServiceDefaults.Auth;
using ClaimBridge.ServiceDefaults.Caching;
using ClaimBridge.ServiceDefaults.Data;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.ServiceDefaults.Phi;
using ClaimBridge.ServiceDefaults.Storage;
using Microsoft.EntityFrameworkCore;
using Quartz;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("payers-gateway");
builder.AddClaimBridgeAuth();

var config = builder.Configuration;
builder.Services.AddDbContext<PayersDb>(o => o.UseSqlServer(config.GetConnectionString("PayersDb")));
builder.Services.AddPhiProtection(config);
builder.Services.AddClaimBridgeCaching(config);
builder.Services.AddClaimBridgeBlobStorage(config);
builder.Services.AddClaimBridgeMessaging<PayersDb>(config, "payers-gateway")
    .Consume<ClaimReadyHandler>("payers-gateway-submissions", Topics.ClaimReady);

foreach (var payer in SeedIds.Payers)
    builder.Services.Configure<PayerOptions>(payer, config.GetSection($"Payers:{payer}"));

builder.Services.AddHttpClient<ClaimsClient>(c => c.BaseAddress = new Uri(config["Services:Claims"]!))
    .AddStandardResilienceHandler();
builder.Services.AddSingleton<PayerHealthTracker>();
builder.Services.AddHttpClient<PayerAAdapter>(c => c.BaseAddress = new Uri(config[$"Payers:{SeedIds.PayerA}:BaseUrl"]!))
    .AddPayerPipeline(SeedIds.PayerA);
builder.Services.AddHttpClient<PayerBAdapter>(c => c.BaseAddress = new Uri(config[$"Payers:{SeedIds.PayerB}:BaseUrl"]!))
    .AddPayerPipeline(SeedIds.PayerB);
builder.Services.AddHttpClient<PayerCAdapter>(c => c.BaseAddress = new Uri(config[$"Payers:{SeedIds.PayerC}:BaseUrl"]!))
    .AddPayerPipeline(SeedIds.PayerC);
builder.Services.AddScoped<IPayerAdapter>(sp => sp.GetRequiredService<PayerAAdapter>());
builder.Services.AddScoped<IPayerAdapter>(sp => sp.GetRequiredService<PayerBAdapter>());
builder.Services.AddScoped<IPayerAdapter>(sp => sp.GetRequiredService<PayerCAdapter>());

builder.Services.AddScoped<PayerAdapterRegistry>();
builder.Services.AddScoped<SubmissionService>();
builder.Services.AddScoped<StatusInquiryService>();
builder.Services.AddScoped<EligibilityService>();

builder.Services.AddQuartz(q =>
{
    var deferred = new JobKey("deferred-submissions");
    q.AddJob<DeferredSubmissionJob>(deferred);
    q.AddTrigger(t => t.ForJob(deferred).StartNow().WithSimpleSchedule(s => s.WithIntervalInSeconds(10).RepeatForever()));

    var batchWriter = new JobKey("payer-b-batch-writer");
    q.AddJob<PayerBBatchWriterJob>(batchWriter);
    q.AddTrigger(t => t.ForJob(batchWriter).StartNow()
        .WithSimpleSchedule(s => s.WithIntervalInSeconds(config.GetValue("Files:BatchWriterSeconds", 15)).RepeatForever()));

    var inquiries = new JobKey("claim-status-inquiries");
    q.AddJob<ClaimStatusInquiryJob>(inquiries);
    q.AddTrigger(t => t.ForJob(inquiries).StartNow()
        .WithSimpleSchedule(s => s.WithIntervalInSeconds(config.GetValue("Inquiry:ScanSeconds", 60)).RepeatForever()));

    var polling = new JobKey("payer-file-polling");
    q.AddJob<PayerFilePollingJob>(polling);
    q.AddTrigger(t => t.ForJob(polling).StartNow()
        .WithSimpleSchedule(s => s.WithIntervalInSeconds(config.GetValue("Files:PollSeconds", 10)).RepeatForever()));
});
builder.Services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true);

builder.Services.AddHealthChecks().AddSqlServerReadiness(config, "PayersDb");
builder.Services.AddControllers();

var app = builder.Build();

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    await app.MigrateAsync<PayersDb>();

app.Run();

public partial class Program;
