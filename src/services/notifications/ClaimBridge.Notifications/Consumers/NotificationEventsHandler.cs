using ClaimBridge.Contracts.Events;
using ClaimBridge.Notifications.Data;
using ClaimBridge.Notifications.Email;
using ClaimBridge.Notifications.Webhooks;
using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Notifications.Consumers;

/// <summary>Fans claim and remittance events out into one pending delivery per matching subscription.</summary>
public class NotificationEventsHandler(
    NotificationsDb db,
    ProvidersClient providers,
    EmailNotifier email,
    TimeProvider clock) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        if (envelope.Type == WebhookSubscriptionChanged.EventType)
        {
            await providers.InvalidateAsync(envelope.DataAs<WebhookSubscriptionChanged>().OrgId, ct);
            return;
        }

        (Guid OrgId, string Type, object Data)? webhook = null;
        if (envelope.Type == ClaimStatusChanged.EventType)
        {
            var e = envelope.DataAs<ClaimStatusChanged>();
            if (WebhookEvents.ForStatus(e.ToStatus) is { } type && e.FromStatus != e.ToStatus)
            {
                webhook = (e.OrgId, type, WebhookEvents.ClaimData(e));
                if (type is WebhookEvents.ClaimDenied or WebhookEvents.ClaimRejected)
                    await email.QueueClaimProblemAsync(e, type, ct);
            }
        }
        else if (envelope.Type == PaymentPosted.EventType)
        {
            var e = envelope.DataAs<PaymentPosted>();
            webhook = (e.OrgId, WebhookEvents.RemittancePosted, WebhookEvents.RemittanceData(e));
        }

        if (webhook is not { } w) return;

        var subscriptions = await providers.SubscriptionsAsync(w.OrgId, ct);
        var now = clock.GetUtcNow();
        foreach (var sub in subscriptions.Where(s => s.Active && s.Events.Contains(w.Type)))
        {
            if (await db.Deliveries.AnyAsync(d => d.SourceEventId == envelope.Id && d.SubscriptionId == sub.Id, ct)) continue;
            var id = Guid.NewGuid();
            db.Deliveries.Add(new WebhookDelivery
            {
                Id = id, SubscriptionId = sub.Id, OrgId = sub.OrgId, SourceEventId = envelope.Id, EventType = w.Type, Url = sub.Url,
                Payload = WebhookEvents.Payload(id, w.Type, envelope.OccurredAt, w.Data), Status = DeliveryStatus.Pending,
                NextAttemptAt = now, CreatedAt = now
            });
        }
    }
}
