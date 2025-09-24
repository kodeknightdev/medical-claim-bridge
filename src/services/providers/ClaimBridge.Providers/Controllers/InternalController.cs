using ClaimBridge.Contracts.Events;
using ClaimBridge.Providers.Data;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.ServiceDefaults.Phi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Providers.Controllers;

/// <summary>
/// Service-to-service lookups. Not routed by the gateway; the cluster NetworkPolicy keeps these internal.
/// </summary>
[ApiController]
[Route("internal")]
[ApiExplorerSettings(GroupName = "internal")]
public class InternalController(ProvidersDb db, IPhiProtector protector, IEventOutbox outbox, TimeProvider clock) : ControllerBase
{
    public sealed record EnrollmentCheck(Guid OrgId, string PayerId, bool Active, string? Status);

    [HttpGet("enrollments/check")]
    public async Task<EnrollmentCheck> CheckEnrollment([FromQuery] Guid orgId, [FromQuery] string payerId)
    {
        var e = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(x => x.OrgId == orgId && x.PayerId == payerId);
        return new EnrollmentCheck(orgId, payerId, e?.Status == EnrollmentStatus.Active, e?.Status.ToString());
    }

    public sealed record WebhookTarget(Guid Id, Guid OrgId, string Url, string[] Events, bool Active, string SigningSecret);

    public sealed record DisableRequest(string Reason);

    [HttpGet("webhooks/by-org/{orgId:guid}")]
    public async Task<IEnumerable<WebhookTarget>> WebhooksForOrg(Guid orgId)
    {
        var hooks = await db.Webhooks.AsNoTracking().Where(w => w.OrgId == orgId).ToListAsync();
        return hooks.Select(w => new WebhookTarget(w.Id, w.OrgId, w.Url, w.EventList, w.Active,
            protector.Unprotect(w.SigningSecretProtected)));
    }

    [HttpPost("webhooks/{id:guid}/disable")]
    public async Task<IActionResult> DisableWebhook(Guid id, DisableRequest request)
    {
        var hook = await db.Webhooks.FindAsync(id);
        if (hook is null) return NotFound();
        if (!hook.Active) return NoContent();
        hook.Active = false;
        hook.DisabledAt = clock.GetUtcNow();
        hook.DisabledReason = request.Reason;
        db.AuditLog.Add(new AuditLogEntry
        {
            At = clock.GetUtcNow(), Actor = "notifications", Action = "webhook.auto-disabled", Target = id.ToString(), Detail = request.Reason
        });
        outbox.Enqueue(Topics.WebhookSubscriptionChanged, hook.OrgId.ToString(), new WebhookSubscriptionChanged(hook.Id, hook.OrgId, "disabled"));
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("orgs/{id:guid}")]
    public async Task<IActionResult> Org(Guid id)
    {
        var org = await db.Organizations.AsNoTracking().Include(o => o.Providers).FirstOrDefaultAsync(o => o.Id == id);
        return org is null ? NotFound() : Ok(new
        {
            org.Id, org.Name, org.GroupNpi, org.TaxId, org.AddressLine1, org.City, org.State, org.Zip, org.ContactEmail,
            Providers = org.Providers.Select(p => new { p.Npi, p.FirstName, p.LastName, p.Taxonomy })
        });
    }
}
