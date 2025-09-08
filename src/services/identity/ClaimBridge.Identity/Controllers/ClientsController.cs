using System.ComponentModel.DataAnnotations;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.Identity.Auth;
using ClaimBridge.Identity.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Identity.Controllers;

[ApiController]
[Route("api/clients")]
[Authorize(Roles = $"{Roles.Admin},{Roles.OpsAnalyst},{Roles.ProviderAdmin}")]
public class ClientsController(IdentityDb db, ApiClientService clients) : ControllerBase
{
    public sealed record ApiClientDto(string ClientId, Guid OrgId, string DisplayName, string[] Scopes,
        int RateLimitPerMinute, DateTimeOffset CreatedAt, DateTimeOffset? SecretRotatedAt, DateTimeOffset? RevokedAt);

    public sealed record CreateClientRequest(
        [property: Required, StringLength(80, MinimumLength = 3)] string DisplayName,
        [property: Required, MinLength(1)] string[] Scopes,
        [property: Range(1, 6000)] int RateLimitPerMinute = 60,
        Guid? OrgId = null);

    public sealed record SecretResponse(string ClientId, string ClientSecret, string Notice);

    [HttpGet]
    public async Task<IEnumerable<ApiClientDto>> List([FromQuery] Guid? orgId)
    {
        var query = db.ApiClients.AsNoTracking();
        var scopeOrg = User.IsStaff() ? orgId : User.OrgId();
        if (scopeOrg is { } o) query = query.Where(c => c.OrgId == o);
        var rows = await query.OrderBy(c => c.DisplayName).ToListAsync();
        return rows.Select(ToDto);
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Admin},{Roles.ProviderAdmin}")]
    public async Task<ActionResult<SecretResponse>> Create(CreateClientRequest request)
    {
        var orgId = User.IsStaff() ? request.OrgId : User.OrgId();
        if (orgId is null)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["orgId"] = ["orgId is required when an admin creates a client"]
            }));

        try
        {
            var created = await clients.CreateAsync(orgId.Value, request.DisplayName, request.Scopes,
                request.RateLimitPerMinute, User.Identity!.Name ?? "unknown");
            return CreatedAtAction(nameof(List), null,
                new SecretResponse(created.Profile.ClientId, created.ClientSecret, "Store this secret now, it is not shown again."));
        }
        catch (ArgumentException ex)
        {
            return Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }

    [HttpPost("{clientId}/rotate-secret")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.ProviderAdmin}")]
    public async Task<ActionResult<SecretResponse>> Rotate(string clientId)
    {
        var profile = await FindOwnedAsync(clientId);
        if (profile is null) return NotFound();
        if (profile.RevokedAt is not null) return Problem("Client is revoked", statusCode: StatusCodes.Status409Conflict);

        var secret = await clients.RotateSecretAsync(profile, User.Identity!.Name ?? "unknown");
        return new SecretResponse(profile.ClientId, secret, "The previous secret stopped working immediately.");
    }

    [HttpPost("{clientId}/revoke")]
    [Authorize(Roles = $"{Roles.Admin},{Roles.ProviderAdmin}")]
    public async Task<IActionResult> Revoke(string clientId)
    {
        var profile = await FindOwnedAsync(clientId);
        if (profile is null) return NotFound();
        await clients.RevokeAsync(profile, User.Identity!.Name ?? "unknown");
        return NoContent();
    }

    private async Task<ApiClientProfile?> FindOwnedAsync(string clientId)
    {
        var profile = await clients.FindAsync(clientId);
        if (profile is null) return null;
        return User.IsStaff() || profile.OrgId == User.OrgId() ? profile : null;
    }

    private static ApiClientDto ToDto(ApiClientProfile c) => new(c.ClientId, c.OrgId, c.DisplayName, c.ScopeList,
        c.RateLimitPerMinute, c.CreatedAt, c.SecretRotatedAt, c.RevokedAt);
}
