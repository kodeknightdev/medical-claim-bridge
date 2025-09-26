using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Application.Clients;
using ClaimBridge.Claims.Data;
using ClaimBridge.Contracts.Submission;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Claims.Api;

/// <summary>Service-to-service endpoints; not routed by the gateway and fenced by the cluster NetworkPolicy.</summary>
[ApiController]
[Route("internal/claims")]
[ApiExplorerSettings(GroupName = "internal")]
public class InternalClaimsController(ClaimsDb db, ClaimPhi phi, ProvidersClient providers, TimeProvider clock) : ControllerBase
{
    [HttpGet("{id:guid}/submission")]
    public async Task<ActionResult<SubmissionDocument>> Submission(Guid id, CancellationToken ct)
    {
        var claim = await db.Claims.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null) return NotFound();

        var org = await providers.GetOrgAsync(claim.OrgId, ct);
        var patient = phi.Reveal(claim);
        string? originalPayerClaimId = null;
        if (claim.PreviousClaimId is { } prev)
            originalPayerClaimId = await db.Claims.Where(c => c.Id == prev).Select(c => c.PayerClaimId).FirstOrDefaultAsync(ct);

        db.AuditLog.Add(new AuditLogEntry
        {
            At = clock.GetUtcNow(), Actor = "payers-gateway", OrgId = claim.OrgId, Action = "phi.released-for-submission",
            Target = claim.ClaimNumber
        });
        await db.SaveChangesAsync(ct);

        return new SubmissionDocument(
            claim.Id, claim.ClaimNumber, claim.Version, claim.FrequencyCode, claim.OriginalClaimNumber, originalPayerClaimId,
            claim.OrgId, claim.PayerId,
            new BillingProvider(org.Name, claim.BillingNpi, org.TaxId, org.AddressLine1, org.City, org.State, org.Zip),
            claim.RenderingNpi,
            new SubscriberInfo(patient.MemberId, patient.FirstName, patient.LastName, patient.DateOfBirth, patient.Gender),
            claim.PriorAuthNumber, claim.Diagnoses,
            claim.Lines.OrderBy(l => l.LineNumber).Select(l => new SubmissionLine(l.LineNumber, l.ProcedureCode, l.Modifiers, l.Units,
                l.Charge, l.ServiceDate, l.PlaceOfService, l.DxPointers)).ToList(),
            claim.TotalCharge);
    }

    [HttpGet("by-number/{claimNumber}")]
    public async Task<IActionResult> ByNumber(string claimNumber, CancellationToken ct)
    {
        var claim = await db.Claims.AsNoTracking().Where(c => c.ClaimNumber == claimNumber)
            .Select(c => new { c.Id, c.ClaimNumber, c.OrgId, c.PayerId, c.TotalCharge, Status = c.Status.ToString(), c.FirstSubmittedAt })
            .FirstOrDefaultAsync(ct);
        return claim is null ? NotFound() : Ok(claim);
    }
}
