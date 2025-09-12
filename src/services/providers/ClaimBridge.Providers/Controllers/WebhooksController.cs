using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using ClaimBridge.Providers.Data;
using ClaimBridge.ServiceDefaults.Auth;
using ClaimBridge.ServiceDefaults.Phi;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Providers.Controllers;

[ApiController]
[Route("api/webhooks")]
[Authorize]
public class WebhooksController(ProvidersDb db, IPhiProtector protector, TimeProvider clock) : ControllerBase
{
    public static readonly string[] SupportedEvents =
        ["claim.accepted", "claim.rejected", "claim.paid", "claim.denied", "remittance.posted"];

    public sealed record WebhookDto(Guid Id, Guid OrgId, string Url, string[] Events, bool Active,
        DateTimeOffset CreatedAt, DateTimeOffset? DisabledAt, string? DisabledReason);

    public sealed record CreateWebhook([property: Required, Url, StringLength(400)] string Url,
        [property: Required, MinLength(1)] string[] Events, Guid? OrgId = null);

    public sealed record SecretResponse(Guid Id, string SigningSecret, string Notice);

    [HttpGet]
    public async Task<IEnumerable<WebhookDto>> List([FromQuery] Guid? orgId)
    {
        var filter = User.OrgFilter(orgId);
        var query = db.Webhooks.AsNoTracking();
        if (filter is { } f) query = query.Where(w => w.OrgId == f);
        var rows = await query.OrderBy(w => w.CreatedAt).ToListAsync();
        return rows.Select(ToDto);
    }

    [HttpPost]
    [Authorize(Policy = Policies.ProviderAdmin)]
    public async Task<ActionResult<SecretResponse>> Create(CreateWebhook request)
    {
        var orgId = User.IsStaff() ? request.OrgId : User.OrgId();
        if (orgId is null) return Problem("orgId is required", statusCode: StatusCodes.Status400BadRequest);
        var unknown = request.Events.Except(SupportedEvents).ToArray();
        if (unknown.Length > 0)
            return Problem($"Unsupported events: {string.Join(", ", unknown)}", statusCode: StatusCodes.Status400BadRequest);

        var secret = NewSecret();
        var hook = new WebhookSubscription
        {
            Id = Guid.NewGuid(), OrgId = orgId.Value, Url = request.Url, Events = string.Join(',', request.Events.Distinct()),
            SigningSecretProtected = protector.Protect(secret), CreatedAt = clock.GetUtcNow()
        };
        db.Webhooks.Add(hook);
        Audit("webhook.created", hook.Id.ToString());
        await db.SaveChangesAsync();
        return Created($"/api/webhooks/{hook.Id}", new SecretResponse(hook.Id, secret, "Store this signing secret now, it is not shown again."));
    }

    [HttpPost("{id:guid}/rotate-secret")]
    [Authorize(Policy = Policies.ProviderAdmin)]
    public async Task<ActionResult<SecretResponse>> Rotate(Guid id)
    {
        var hook = await FindOwnedAsync(id);
        if (hook is null) return NotFound();
        var secret = NewSecret();
        hook.SigningSecretProtected = protector.Protect(secret);
        Audit("webhook.secret-rotated", hook.Id.ToString());
        await db.SaveChangesAsync();
        return new SecretResponse(hook.Id, secret, "Deliveries are signed with the new secret from now on.");
    }

    [HttpPost("{id:guid}/enable")]
    [Authorize(Policy = Policies.ProviderAdmin)]
    public async Task<IActionResult> Enable(Guid id)
    {
        var hook = await FindOwnedAsync(id);
        if (hook is null) return NotFound();
        hook.Active = true;
        hook.DisabledAt = null;
        hook.DisabledReason = null;
        Audit("webhook.enabled", hook.Id.ToString());
        await db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.ProviderAdmin)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var hook = await FindOwnedAsync(id);
        if (hook is null) return NotFound();
        db.Webhooks.Remove(hook);
        Audit("webhook.deleted", hook.Id.ToString());
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<WebhookSubscription?> FindOwnedAsync(Guid id)
    {
        var hook = await db.Webhooks.FindAsync(id);
        return hook is not null && User.CanAccessOrg(hook.OrgId) ? hook : null;
    }

    private void Audit(string action, string target) =>
        db.AuditLog.Add(new AuditLogEntry { At = clock.GetUtcNow(), Actor = User.Actor(), Action = action, Target = target });

    internal static string NewSecret() => "whsec_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    private static WebhookDto ToDto(WebhookSubscription w) =>
        new(w.Id, w.OrgId, w.Url, w.EventList, w.Active, w.CreatedAt, w.DisabledAt, w.DisabledReason);
}
