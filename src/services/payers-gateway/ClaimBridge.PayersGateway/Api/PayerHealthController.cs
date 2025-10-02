using ClaimBridge.Contracts.Reference;
using ClaimBridge.PayersGateway.Data;
using ClaimBridge.PayersGateway.Resilience;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.PayersGateway.Api;

[ApiController]
[Route("api/payers")]
[Authorize(Policy = Policies.Staff)]
public class PayerHealthController(PayerHealthTracker tracker, PayersDb db) : ControllerBase
{
    public sealed record PayerHealthDto(string PayerId, string Name, string Channel, PayerHealthSnapshot Health, int Queued,
        int Deferred, DateTimeOffset? LastFileAt);

    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IEnumerable<PayerHealthDto>> Health(CancellationToken ct)
    {
        var counts = await db.Submissions.Where(s => s.Status == SubmissionStatus.Queued || s.Status == SubmissionStatus.Deferred)
            .GroupBy(s => new { s.PayerId, s.Status }).Select(g => new { g.Key.PayerId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);
        var lastFiles = await db.Files.Where(f => f.Direction == FileDirection.Inbound)
            .GroupBy(f => f.PayerId).Select(g => new { PayerId = g.Key, At = g.Max(f => f.At) }).ToListAsync(ct);

        return PayerDirectory.All.Select(p => new PayerHealthDto(p.Id, p.Name, p.Channel, tracker.Snapshot(p.Id),
            counts.Where(c => c.PayerId == p.Id && c.Status == SubmissionStatus.Queued).Sum(c => c.Count),
            counts.Where(c => c.PayerId == p.Id && c.Status == SubmissionStatus.Deferred).Sum(c => c.Count),
            lastFiles.FirstOrDefault(f => f.PayerId == p.Id)?.At));
    }

    [HttpGet("files")]
    public async Task<IEnumerable<PayerFile>> Files([FromQuery] string? payerId, CancellationToken ct) =>
        await db.Files.AsNoTracking().Where(f => payerId == null || f.PayerId == payerId)
            .OrderByDescending(f => f.At).Take(100).ToListAsync(ct);
}
