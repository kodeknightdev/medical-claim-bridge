using ClaimBridge.Providers.Data;
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
public class InternalController(ProvidersDb db, EnrollmentService enrollments) : ControllerBase
{
    public sealed record EnrollmentCheck(Guid OrgId, string PayerId, bool Active, string? Status);

    [HttpGet("enrollments/check")]
    public async Task<EnrollmentCheck> CheckEnrollment([FromQuery] Guid orgId, [FromQuery] string payerId)
    {
        var e = await db.Enrollments.AsNoTracking().FirstOrDefaultAsync(x => x.OrgId == orgId && x.PayerId == payerId);
        return new EnrollmentCheck(orgId, payerId, e?.Status == EnrollmentStatus.Active, e?.Status.ToString());
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
