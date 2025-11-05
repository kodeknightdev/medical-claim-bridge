using System.ComponentModel.DataAnnotations;
using ClaimBridge.Providers.Data;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Providers.Controllers;

[ApiController]
[Route("api/enrollments")]
[Authorize]
public class EnrollmentsController(ProvidersDb db, EnrollmentService enrollments) : ControllerBase
{
    public sealed record EnrollmentDto(Guid Id, Guid OrgId, string OrgName, string PayerId, string PayerName,
        string Status, DateTimeOffset RequestedAt, string RequestedBy, DateTimeOffset? DecidedAt, string? DecidedBy, string? Note);

    public sealed record RequestEnrollment([property: Required] string PayerId, Guid? OrgId = null);

    public sealed record Decision([property: Required] EnrollmentStatus Status, [property: StringLength(500)] string? Note);

    [HttpGet]
    public async Task<IEnumerable<EnrollmentDto>> List([FromQuery] Guid? orgId)
    {
        var filter = User.OrgFilter(orgId);
        var query = from e in db.Enrollments.AsNoTracking()
                    join o in db.Organizations on e.OrgId equals o.Id
                    join p in db.Payers on e.PayerId equals p.Id
                    select new { e, OrgName = o.Name, PayerName = p.Name };
        if (filter is { } f) query = query.Where(x => x.e.OrgId == f);
        var rows = await query.OrderBy(x => x.OrgName).ThenBy(x => x.e.PayerId).ToListAsync();
        return rows.Select(x => new EnrollmentDto(x.e.Id, x.e.OrgId, x.OrgName, x.e.PayerId, x.PayerName,
            x.e.Status.ToString(), x.e.RequestedAt, x.e.RequestedBy, x.e.DecidedAt, x.e.DecidedBy, x.e.Note));
    }

    [HttpPost]
    [Authorize(Policy = Policies.ProviderAdmin)]
    public async Task<IActionResult> Create(RequestEnrollment request)
    {
        var orgId = User.IsStaff() ? request.OrgId : User.OrgId();
        if (orgId is null) return Problem("orgId is required", statusCode: StatusCodes.Status400BadRequest);
        if (!await db.Payers.AnyAsync(p => p.Id == request.PayerId))
            return Problem($"Unknown payer {request.PayerId}", statusCode: StatusCodes.Status400BadRequest);

        var result = await enrollments.RequestAsync(orgId.Value, request.PayerId, User.Actor());
        return result is null
            ? Problem("An enrollment for this payer already exists.", statusCode: StatusCodes.Status409Conflict)
            : Accepted(new { result.Id, Status = result.Status.ToString() });
    }

    [HttpPost("{id:guid}/decision")]
    [Authorize(Policy = Policies.Staff)]
    public async Task<IActionResult> Decide(Guid id, Decision decision)
    {
        if (decision.Status == EnrollmentStatus.Pending)
            return Problem("Decision must be Active or Rejected", statusCode: StatusCodes.Status400BadRequest);
        var enrollment = await db.Enrollments.FindAsync(id);
        if (enrollment is null) return NotFound();
        await enrollments.DecideAsync(enrollment, decision.Status, decision.Note, User.Actor());
        return NoContent();
    }
}
