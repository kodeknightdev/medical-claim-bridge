using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Claims.Api;

/// <summary>Machine-to-machine submission for provider systems using the client credentials grant.</summary>
[ApiController]
[Route("api/v1/claims")]
[Tags("Submission API")]
public class SubmissionApiController(ClaimsDb db, ClaimIntakeService intake) : ControllerBase
{
    public sealed record SubmissionAccepted(Guid ClaimId, string ClaimNumber, string Status, string StatusUrl);

    public sealed record ClaimStatusResponse(Guid ClaimId, string ClaimNumber, string Status, DateTimeOffset StatusChangedAt,
        decimal TotalCharge, decimal? PaidAmount, string? PayerClaimId, IReadOnlyList<HistoryDto> History);

    [HttpPost]
    [Authorize(Policy = Policies.ClaimsSubmit)]
    [ServiceFilter(typeof(IdempotencyFilter))]
    [ProducesResponseType<SubmissionAccepted>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Submit(ClaimRequest request, CancellationToken ct)
    {
        var orgId = User.OrgId();
        if (orgId is null) return Problem("The token is not bound to a provider organisation.", statusCode: 403);

        var outcome = await intake.AcceptAsync(request with { OrgId = null }, orgId.Value, ClaimSource.Api,
            User.ClientId() ?? User.Actor(), null, ct);
        if (outcome.Problem is { } p) return Problem(p.Detail, title: p.Title, statusCode: p.Status);

        var claim = outcome.Claim!;
        return Accepted($"/api/v1/claims/{claim.Id}",
            new SubmissionAccepted(claim.Id, claim.ClaimNumber, claim.Status.ToString(), $"/api/v1/claims/{claim.Id}"));
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ClaimsRead)]
    public async Task<ActionResult<ClaimStatusResponse>> Status(Guid id, CancellationToken ct)
    {
        var claim = await db.Claims.AsNoTracking().Include(c => c.History).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (claim is null || !User.CanAccessOrg(claim.OrgId)) return NotFound();
        return new ClaimStatusResponse(claim.Id, claim.ClaimNumber, claim.Status.ToString(), claim.StatusChangedAt, claim.TotalCharge,
            claim.PaidAmount, claim.PayerClaimId, claim.History.OrderBy(h => h.Sequence).Select(h => h.ToDto()).ToList());
    }
}
