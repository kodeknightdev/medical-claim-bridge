using ClaimBridge.Notifications.Data;
using ClaimBridge.Notifications.Webhooks;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Notifications.Api;

[ApiController]
[Route("api/webhook-deliveries")]
[Authorize(Policy = Policies.ClaimsRead)]
public class DeliveriesController(NotificationsDb db, WebhookSender sender, TimeProvider clock) : ControllerBase
{
    public sealed record DeliveryDto(Guid Id, Guid SubscriptionId, string EventType, string Url, string Status, int Attempts,
        int? LastStatusCode, string? LastError, DateTimeOffset CreatedAt, DateTimeOffset? DeliveredAt, DateTimeOffset NextAttemptAt);

    public sealed record DeliveryDetail(DeliveryDto Delivery, string Payload, IReadOnlyList<DeliveryAttempt> Attempts);

    [HttpGet]
    public async Task<IEnumerable<DeliveryDto>> List([FromQuery] Guid? subscriptionId, [FromQuery] string? status, [FromQuery] Guid? orgId,
        CancellationToken ct)
    {
        var query = db.Deliveries.AsNoTracking();
        if (User.OrgFilter(orgId) is { } org) query = query.Where(d => d.OrgId == org);
        if (subscriptionId is { } sub) query = query.Where(d => d.SubscriptionId == sub);
        if (Enum.TryParse<DeliveryStatus>(status, out var s)) query = query.Where(d => d.Status == s);
        var rows = await query.OrderByDescending(d => d.CreatedAt).Take(200).ToListAsync(ct);
        return rows.Select(ToDto);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DeliveryDetail>> Get(Guid id, CancellationToken ct)
    {
        var d = await db.Deliveries.AsNoTracking().Include(x => x.AttemptLog).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null || !User.CanAccessOrg(d.OrgId)) return NotFound();
        return new DeliveryDetail(ToDto(d), d.Payload, d.AttemptLog.OrderBy(a => a.Number).ToList());
    }

    /// <summary>Manual redelivery: sends now, signed with the current secret and a fresh timestamp.</summary>
    [HttpPost("{id:guid}/redeliver")]
    [Authorize(Policy = Policies.ProviderAdmin)]
    public async Task<ActionResult<DeliveryDto>> Redeliver(Guid id, CancellationToken ct)
    {
        var d = await db.Deliveries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null || !User.CanAccessOrg(d.OrgId)) return NotFound();

        d.Status = DeliveryStatus.Pending;
        await sender.AttemptAsync(db, d, manual: true, ct);
        if (d.Status == DeliveryStatus.Pending)
            d.NextAttemptAt = clock.GetUtcNow().AddMinutes(1);
        db.AuditLog.Add(new AuditLogEntry { At = clock.GetUtcNow(), Actor = User.Actor(), Action = "webhook.redelivered", Target = id.ToString() });
        await db.SaveChangesAsync(ct);
        return ToDto(d);
    }

    [HttpGet("stats")]
    public async Task<object> Stats([FromQuery] int days = 7, CancellationToken ct = default)
    {
        var since = clock.GetUtcNow().AddDays(-Math.Clamp(days, 1, 90));
        var query = db.Deliveries.AsNoTracking().Where(d => d.CreatedAt >= since);
        if (User.OrgFilter() is { } org) query = query.Where(d => d.OrgId == org);
        var byStatus = await query.GroupBy(d => d.Status).Select(g => new { Status = g.Key.ToString(), Count = g.Count() }).ToListAsync(ct);
        var subs = await db.SubscriptionHealth.AsNoTracking().ToListAsync(ct);
        return new { byStatus, subscriptions = subs.Where(s => User.CanAccessOrg(s.OrgId)) };
    }

    private static DeliveryDto ToDto(WebhookDelivery d) => new(d.Id, d.SubscriptionId, d.EventType, d.Url, d.Status.ToString(),
        d.Attempts, d.LastStatusCode, d.LastError, d.CreatedAt, d.DeliveredAt, d.NextAttemptAt);
}
