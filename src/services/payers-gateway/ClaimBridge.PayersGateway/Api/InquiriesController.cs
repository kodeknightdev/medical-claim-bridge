using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.PayersGateway.Consumers;
using ClaimBridge.PayersGateway.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.PayersGateway.Api;

[ApiController]
[Route("internal/status-inquiries")]
[ApiExplorerSettings(GroupName = "internal")]
public class InquiriesController(StatusInquiryService inquiries, PayersDb db) : ControllerBase
{
    public sealed record InquiryRequest(Guid ClaimId, string ClaimNumber, string PayerId);

    /// <summary>Called by the claims process manager when an acknowledgement is overdue.</summary>
    [HttpPost]
    public async Task<IActionResult> Inquire(InquiryRequest request, CancellationToken ct)
    {
        var submission = await db.Submissions.Where(s => s.ClaimId == request.ClaimId)
            .OrderByDescending(s => s.Attempt).FirstOrDefaultAsync(ct);
        if (submission is null) return NotFound();

        var answer = await inquiries.InquireAsync(submission, ct);
        await db.SaveChangesAsync(ct);
        return Ok(answer);
    }
}
