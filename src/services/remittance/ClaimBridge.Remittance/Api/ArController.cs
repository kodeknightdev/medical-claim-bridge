using ClaimBridge.Remittance.Data;
using ClaimBridge.Remittance.Domain;
using ClaimBridge.Remittance.Jobs;
using ClaimBridge.ServiceDefaults.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Remittance.Api;

[ApiController]
[Route("api/ar")]
[Authorize(Policy = Policies.ClaimsRead)]
public class ArController(RemittanceDb db, TimeProvider clock) : ControllerBase
{
    public sealed record PayerAr(string PayerId, AgeingBuckets Buckets, int OpenClaims);

    public sealed record ArSummary(DateOnly AsOf, AgeingBuckets Buckets, IReadOnlyList<PayerAr> ByPayer, int OpenClaims,
        decimal DaysInAr, decimal PatientResponsibilityOutstanding);

    [HttpGet("summary")]
    public async Task<ArSummary> Summary([FromQuery] Guid? orgId, CancellationToken ct)
    {
        var asOf = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var query = db.Balances.AsNoTracking();
        if (User.OrgFilter(orgId) is { } org) query = query.Where(b => b.OrgId == org);

        var open = await query.Where(b => b.Status == BalanceStatus.Open || b.Status == BalanceStatus.Denied).ToListAsync(ct);
        var since = clock.GetUtcNow().AddDays(-90);
        var charges90 = await query.Where(b => b.OpenedAt >= since).SumAsync(b => (decimal?)b.Charge, ct) ?? 0;
        var patient = await query.Where(b => b.Status != BalanceStatus.Closed).SumAsync(b => (decimal?)b.PatientResponsibility, ct) ?? 0;

        var buckets = Ageing.Calculate(open, asOf);
        return new ArSummary(asOf, buckets,
            open.GroupBy(b => b.PayerId).Select(g => new PayerAr(g.Key, Ageing.Calculate(g, asOf), g.Count(b => b.PayerOutstanding > 0)))
                .OrderBy(p => p.PayerId).ToList(),
            open.Count(b => b.PayerOutstanding > 0), Ageing.DaysInAr(buckets.Total, charges90), patient);
    }

    [HttpGet("open-claims")]
    public async Task<IEnumerable<object>> OpenClaims([FromQuery] Guid? orgId, [FromQuery] string? bucket, [FromQuery] string? payerId,
        CancellationToken ct)
    {
        var asOf = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var query = db.Balances.AsNoTracking().Where(b => b.PayerOutstanding > 0 && b.Status != BalanceStatus.Closed);
        if (User.OrgFilter(orgId) is { } org) query = query.Where(b => b.OrgId == org);
        if (!string.IsNullOrEmpty(payerId)) query = query.Where(b => b.PayerId == payerId);

        var rows = await query.OrderBy(b => b.OpenedAt).Take(500).ToListAsync(ct);
        return rows.Select(b => new
        {
            b.ClaimId, b.ClaimNumber, b.PayerId, b.Charge, b.Paid, b.Adjusted, b.PatientResponsibility, b.PayerOutstanding,
            Status = b.Status.ToString(), b.OpenedAt, Bucket = Ageing.BucketFor(b.OpenedAt, asOf),
            AgeDays = asOf.DayNumber - DateOnly.FromDateTime(b.OpenedAt.UtcDateTime).DayNumber
        })
            .Where(b => bucket is null || b.Bucket == bucket)
            .Take(200);
    }

    [HttpGet("snapshots")]
    public async Task<IEnumerable<ArSnapshot>> Snapshots([FromQuery] Guid? orgId, [FromQuery] int days = 30, CancellationToken ct = default)
    {
        var from = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-Math.Clamp(days, 1, 365));
        var query = db.ArSnapshots.AsNoTracking().Where(s => s.SnapshotDate >= from);
        if (User.OrgFilter(orgId) is { } org) query = query.Where(s => s.OrgId == org);
        return await query.OrderBy(s => s.SnapshotDate).ToListAsync(ct);
    }

    /// <summary>Hook for the Kubernetes CronJob; internal only.</summary>
    [HttpPost("~/internal/jobs/ar-snapshot")]
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "internal")]
    public async Task<IActionResult> RunSnapshot([FromServices] ArSnapshotService snapshots, CancellationToken ct) =>
        Ok(new { rows = await snapshots.TakeAsync(ct) });
}
