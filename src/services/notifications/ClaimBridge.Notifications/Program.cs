using ClaimBridge.Contracts.Events;
using ClaimBridge.Notifications.Consumers;
using ClaimBridge.Notifications.Data;
using ClaimBridge.Notifications.Email;
using ClaimBridge.Notifications.Webhooks;
using ClaimBridge.ServiceDefaults;
using ClaimBridge.ServiceDefaults.Auth;
using ClaimBridge.ServiceDefaults.Caching;
using ClaimBridge.ServiceDefaults.Data;
using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults("notifications");
builder.AddClaimBridgeAuth();

var config = builder.Configuration;
builder.Services.AddDbContext<NotificationsDb>(o => o.UseSqlServer(config.GetConnectionString("NotificationsDb")));
builder.Services.AddClaimBridgeCaching(config);
builder.Services.AddClaimBridgeMessaging<NotificationsDb>(config, "notifications")
    .Consume<NotificationEventsHandler>("notifications", Topics.ClaimStatusChanged, Topics.PaymentPosted, Topics.WebhookSubscriptionChanged);

builder.Services.Configure<WebhookOptions>(config.GetSection("Webhooks"));
builder.Services.Configure<SmtpOptions>(config.GetSection("Smtp"));
builder.Services.AddHttpClient<ProvidersClient>(c => c.BaseAddress = new Uri(config["Services:Providers"]!))
    .AddStandardResilienceHandler();
builder.Services.AddHttpClient(WebhookSender.HttpClientName, c =>
{
    c.Timeout = TimeSpan.FromSeconds(config.GetValue("Webhooks:TimeoutSeconds", 10));
    c.DefaultRequestHeaders.UserAgent.ParseAdd("claim-bridge-webhooks/1.0");
});
builder.Services.AddScoped<WebhookSender>();
builder.Services.AddScoped<EmailNotifier>();
builder.Services.AddSingleton<WebhookMetrics>();
builder.Services.AddHostedService<WebhookDispatcher>();

builder.Services.AddHealthChecks().AddSqlServerReadiness(config, "NotificationsDb");
builder.Services.AddControllers();

var app = builder.Build();

app.MapServiceDefaults();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

if (app.Configuration.GetValue<bool>("Seed:Enabled"))
    await app.MigrateAsync<NotificationsDb>();

app.Run();

public partial class Program;
