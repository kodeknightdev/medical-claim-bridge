using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using ClaimBridge.Contracts.Webhooks;
using ClaimBridge.Notifications.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimBridge.Notifications.Webhooks;

/// <summary>Sends due deliveries, signs each attempt freshly, and applies backoff and auto-disable.</summary>
public sealed class WebhookDispatcher(
    IServiceScopeFactory scopes,
    IOptions<WebhookOptions> options,
    TimeProvider clock,
    ILogger<WebhookDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchDueAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Webhook dispatch loop failed");
            }
            await Task.Delay(options.Value.DispatchIntervalMilliseconds, stoppingToken);
        }
    }

    public async Task DispatchDueAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDb>();
        var sender = scope.ServiceProvider.GetRequiredService<WebhookSender>();
        var now = clock.GetUtcNow();

        var due = await db.Deliveries.Where(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt <= now)
            .OrderBy(d => d.NextAttemptAt).Take(20).ToListAsync(ct);
        foreach (var delivery in due)
        {
            await sender.AttemptAsync(db, delivery, manual: false, ct);
            await db.SaveChangesAsync(ct);
        }
    }
}

public class WebhookSender(
    IHttpClientFactory httpFactory,
    ProvidersClient providers,
    IOptions<WebhookOptions> options,
    WebhookMetrics metrics,
    TimeProvider clock,
    ILogger<WebhookSender> logger)
{
    public const string HttpClientName = "webhooks";

    public async Task AttemptAsync(NotificationsDb db, WebhookDelivery delivery, bool manual, CancellationToken ct)
    {
        var health = await db.SubscriptionHealth.FindAsync([delivery.SubscriptionId], ct);
        if (health is null)
        {
            health = new SubscriptionHealth { SubscriptionId = delivery.SubscriptionId, OrgId = delivery.OrgId };
            db.SubscriptionHealth.Add(health);
        }

        var target = (await providers.SubscriptionsAsync(delivery.OrgId, ct)).FirstOrDefault(s => s.Id == delivery.SubscriptionId);
        if (target is null || (!target.Active && !manual))
        {
            delivery.Status = DeliveryStatus.Abandoned;
            delivery.LastError = target is null ? "Subscription deleted" : "Subscription disabled";
            return;
        }

        var now = clock.GetUtcNow();
        var watch = Stopwatch.StartNew();
        int? status = null;
        string? error = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, target.Url)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
            };
            request.Headers.Add(WebhookSignature.HeaderName, WebhookSignature.Sign(target.SigningSecret, now.ToUnixTimeSeconds(), delivery.Payload));
            request.Headers.Add(WebhookSignature.EventHeader, delivery.EventType);
            request.Headers.Add(WebhookSignature.DeliveryHeader, delivery.Id.ToString());

            using var response = await httpFactory.CreateClient(HttpClientName).SendAsync(request, ct);
            status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode) error = $"HTTP {status}";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            error = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
        }

        delivery.Attempts++;
        delivery.LastStatusCode = status;
        delivery.LastError = error;
        delivery.AttemptLog.Add(new DeliveryAttempt
        {
            DeliveryId = delivery.Id, Number = delivery.Attempts, At = now, StatusCode = status,
            DurationMs = (int)watch.ElapsedMilliseconds, Error = error, Manual = manual
        });

        if (error is null)
        {
            delivery.Status = DeliveryStatus.Succeeded;
            delivery.DeliveredAt = now;
            health.ConsecutiveFailures = 0;
            health.LastSuccessAt = now;
            metrics.Delivered(delivery.EventType, true);
            return;
        }

        metrics.Delivered(delivery.EventType, false);
        health.ConsecutiveFailures++;
        health.LastFailureAt = now;

        if (delivery.Attempts >= WebhookBackoff.MaxAttempts(options.Value))
            delivery.Status = DeliveryStatus.Abandoned;
        else
            delivery.NextAttemptAt = now + WebhookBackoff.NextDelay(delivery.Attempts, options.Value);

        if (WebhookBackoff.ShouldDisable(health.ConsecutiveFailures, options.Value) && health.DisabledAt is null)
        {
            health.DisabledAt = now;
            logger.LogWarning("Disabling webhook {SubscriptionId} after {Failures} consecutive failures", delivery.SubscriptionId,
                health.ConsecutiveFailures);
            try
            {
                await providers.DisableAsync(delivery.SubscriptionId, $"Auto-disabled after {health.ConsecutiveFailures} consecutive failures", ct);
                await providers.InvalidateAsync(delivery.OrgId, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Could not disable subscription in providers: {Error}", ex.Message);
            }
        }
    }
}

public sealed class WebhookMetrics
{
    private readonly Counter<long> _deliveries;

    public WebhookMetrics(IMeterFactory meters)
    {
        _deliveries = meters.Create("ClaimBridge.Notifications")
            .CreateCounter<long>("webhook.deliveries", description: "Webhook delivery attempts by outcome");
    }

    public void Delivered(string eventType, bool success) =>
        _deliveries.Add(1, new("event", eventType), new("outcome", success ? "success" : "failure"));
}
