using ClaimBridge.Remittance.Data;
using ClaimBridge.Remittance.Domain;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace ClaimBridge.Remittance.Jobs;

/// <summary>Nightly A/R snapshot per organisation and payer, so ageing trends can be charted over time.</summary>
[DisallowConcurrentExecution]
public class ArSnapshotJob(ArSnapshotService snapshots) : IJob
{
    public Task Execute(IJobExecutionContext context) => snapshots.TakeAsync(context.CancellationToken);
}

public class ArSnapshotService(RemittanceDb db, TimeProvider clock, ILogger<ArSnapshotService> logger)
{
    public async Task<int> TakeAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var open = await db.Balances.AsNoTracking()
            .Where(b => b.PayerOutstanding > 0 && (b.Status == BalanceStatus.Open || b.Status == BalanceStatus.Denied))
            .ToListAsync(ct);

        await db.ArSnapshots.Where(s => s.SnapshotDate == today).ExecuteDeleteAsync(ct);
        var rows = open.GroupBy(b => (b.OrgId, b.PayerId)).Select(g =>
        {
            var buckets = Ageing.Calculate(g, today);
            return new ArSnapshot
            {
                SnapshotDate = today, OrgId = g.Key.OrgId, PayerId = g.Key.PayerId, Days0To30 = buckets.Days0To30,
                Days31To60 = buckets.Days31To60, Days61To90 = buckets.Days61To90, Days90Plus = buckets.Days90Plus,
                OpenClaims = g.Count(), TotalOutstanding = buckets.Total
            };
        }).ToList();
        db.ArSnapshots.AddRange(rows);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("A/R snapshot for {Date}: {Rows} org/payer rows", today, rows.Count);
        return rows.Count;
    }
}
