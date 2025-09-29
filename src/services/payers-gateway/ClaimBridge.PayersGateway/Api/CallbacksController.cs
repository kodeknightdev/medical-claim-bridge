using System.Security.Cryptography;
using System.Text;
using ClaimBridge.Contracts.Reference;
using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.PayersGateway.Adapters.PayerA;
using ClaimBridge.PayersGateway.Consumers;
using ClaimBridge.PayersGateway.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClaimBridge.PayersGateway.Api;

[ApiController]
[Route("api/payers/payer-a/callbacks")]
[ApiExplorerSettings(GroupName = "payer-callbacks")]
public class CallbacksController(PayersDb db, SubmissionService submissions, IOptionsMonitor<PayerOptions> options) : ControllerBase
{
    /// <summary>Northwind posts adjudication updates here with a shared token header.</summary>
    [HttpPost("adjudication")]
    public async Task<IActionResult> Adjudication(NwCallback callback, [FromHeader(Name = "X-Northwind-Token")] string? token,
        CancellationToken ct)
    {
        var expected = options.Get(SeedIds.PayerA).CallbackToken;
        if (string.IsNullOrEmpty(expected) || token is null ||
            !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expected)))
            return Unauthorized();

        var submission = await db.Submissions
            .Where(s => s.PayerId == SeedIds.PayerA && (s.PayerClaimId == callback.TrackingId || s.ClaimNumber == callback.ClaimRef))
            .OrderByDescending(s => s.Attempt).FirstOrDefaultAsync(ct);
        if (submission is null) return NotFound();

        var answer = PayerAAdapter.Translate(callback.Status, callback.Outcome, callback.Note, callback.TrackingId);
        submissions.PublishStatus(submission, answer, "payer-callback");
        await db.SaveChangesAsync(ct);
        return Accepted();
    }
}
