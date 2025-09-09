using System.ComponentModel.DataAnnotations;
using ClaimBridge.Contracts.Validation;
using ClaimBridge.Providers.Data;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Providers.Controllers;

[ApiController]
[Route("api/orgs")]
[Authorize]
public class OrgsController(ProvidersDb db, TimeProvider clock) : ControllerBase
{
    public sealed record OrgDto(Guid Id, string Name, string GroupNpi, string TaxId, string Taxonomy,
        string AddressLine1, string City, string State, string Zip, string ContactEmail, int ProviderCount);

    public sealed record RenderingProviderDto(Guid Id, string Npi, string FirstName, string LastName, string Taxonomy, bool Active);

    public sealed record CreateProviderRequest(
        [property: Required, StringLength(10, MinimumLength = 10)] string Npi,
        [property: Required, StringLength(60)] string FirstName,
        [property: Required, StringLength(60)] string LastName,
        [property: Required, StringLength(10)] string Taxonomy);

    [HttpGet]
    public async Task<IEnumerable<OrgDto>> List()
    {
        var filter = User.OrgFilter();
        var query = db.Organizations.AsNoTracking();
        if (filter is { } orgId) query = query.Where(o => o.Id == orgId);
        return await query.OrderBy(o => o.Name).Select(o => new OrgDto(o.Id, o.Name, o.GroupNpi, o.TaxId, o.Taxonomy,
            o.AddressLine1, o.City, o.State, o.Zip, o.ContactEmail, o.Providers.Count)).ToListAsync();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrgDto>> Get(Guid id)
    {
        if (!User.CanAccessOrg(id)) return NotFound();
        var org = await db.Organizations.AsNoTracking().Where(o => o.Id == id)
            .Select(o => new OrgDto(o.Id, o.Name, o.GroupNpi, o.TaxId, o.Taxonomy, o.AddressLine1, o.City,
                o.State, o.Zip, o.ContactEmail, o.Providers.Count))
            .FirstOrDefaultAsync();
        return org is null ? NotFound() : org;
    }

    [HttpGet("{id:guid}/providers")]
    public async Task<ActionResult<IEnumerable<RenderingProviderDto>>> Providers(Guid id)
    {
        if (!User.CanAccessOrg(id)) return NotFound();
        return await db.RenderingProviders.AsNoTracking().Where(p => p.OrgId == id)
            .OrderBy(p => p.LastName)
            .Select(p => new RenderingProviderDto(p.Id, p.Npi, p.FirstName, p.LastName, p.Taxonomy, p.Active))
            .ToListAsync();
    }

    [HttpPost("{id:guid}/providers")]
    [Authorize(Policy = Policies.ProviderAdmin)]
    public async Task<ActionResult<RenderingProviderDto>> AddProvider(Guid id, CreateProviderRequest request)
    {
        if (!User.CanAccessOrg(id) || !await db.Organizations.AnyAsync(o => o.Id == id)) return NotFound();
        if (!Npi.IsValid(request.Npi))
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["npi"] = ["NPI check digit is invalid (Luhn with 80840 prefix)."]
            }));
        if (await db.RenderingProviders.AnyAsync(p => p.Npi == request.Npi))
            return Problem("A provider with this NPI already exists.", statusCode: StatusCodes.Status409Conflict);

        var provider = new RenderingProvider
        {
            Id = Guid.NewGuid(), OrgId = id, Npi = request.Npi, FirstName = request.FirstName,
            LastName = request.LastName, Taxonomy = request.Taxonomy
        };
        db.RenderingProviders.Add(provider);
        db.AuditLog.Add(new AuditLogEntry
        {
            At = clock.GetUtcNow(), Actor = User.Actor(), Action = "provider.created", Target = provider.Npi
        });
        await db.SaveChangesAsync();
        return CreatedAtAction(nameof(Providers), new { id },
            new RenderingProviderDto(provider.Id, provider.Npi, provider.FirstName, provider.LastName, provider.Taxonomy, true));
    }
}

[ApiController]
[Route("api/payers")]
[Authorize]
public class PayersController(ProvidersDb db) : ControllerBase
{
    [HttpGet]
    public async Task<IEnumerable<Payer>> List() =>
        await db.Payers.AsNoTracking().OrderBy(p => p.Id).ToListAsync();
}
