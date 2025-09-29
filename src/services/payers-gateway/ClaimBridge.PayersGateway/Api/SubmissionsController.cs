using ClaimBridge.PayersGateway.Data;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.PayersGateway.Api;

[ApiController]
[Route("api/submissions")]
[Authorize(Policy = Policies.ClaimsRead)]
public class SubmissionsController(PayersDb db) : ControllerBase
{
    public sealed record SubmissionDto(Guid Id, int Attempt, string PayerId, string Channel, string Status, string? PayerClaimId,
        string? FileName, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, DateTimeOffset? AckAt, string? RejectReasons, string? LastError);

    public sealed record StatusEntryDto(string Kind, string Status, string? Detail, DateTimeOffset At);

    public sealed record ClaimPayerHistory(Guid ClaimId, IReadOnlyList<SubmissionDto> Submissions, IReadOnlyList<StatusEntryDto> History);

    [HttpGet("by-claim/{claimId:guid}")]
    public async Task<ActionResult<ClaimPayerHistory>> ByClaim(Guid claimId, CancellationToken ct)
    {
        var subs = await db.Submissions.AsNoTracking().Where(s => s.ClaimId == claimId).OrderBy(s => s.Attempt).ToListAsync(ct);
        if (subs.Count > 0 && !User.CanAccessOrg(subs[0].OrgId)) return NotFound();

        var history = await db.StatusEntries.AsNoTracking().Where(h => h.ClaimId == claimId).OrderBy(h => h.At)
            .Select(h => new StatusEntryDto(h.Kind, h.Status, h.Detail, h.At)).ToListAsync(ct);
        return new ClaimPayerHistory(claimId,
            subs.Select(s => new SubmissionDto(s.Id, s.Attempt, s.PayerId, s.Channel, s.Status.ToString(), s.PayerClaimId, s.FileName,
                s.CreatedAt, s.SentAt, s.AckAt, s.RejectReasons, s.LastError)).ToList(),
            history);
    }
}
