using System.Text.Json;
using ClaimBridge.Contracts.Events;
using ClaimBridge.Remittance.Data;
using ClaimBridge.Remittance.Domain;
using ClaimBridge.X12.Transactions;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace ClaimBridge.Remittance.Jobs;

/// <summary>An ERA can beat the claim's status event here; retry parked lines once the claim becomes known.</summary>
[DisallowConcurrentExecution]
public class ReconciliationRetryJob(RemittanceDb db, PostingService posting, TimeProvider clock) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var open = await db.Unmatched.Where(u => u.ResolvedAt == null && u.Reason.StartsWith("No submitted claim"))
            .OrderBy(u => u.CreatedAt).Take(50).ToListAsync(ct);

        foreach (var line in open)
        {
            if (!await db.ExpectedClaims.AnyAsync(c => c.ClaimNumber == line.ClaimNumber && c.PayerId == line.PayerId, ct)) continue;
            var file = await db.Files.FirstAsync(f => f.Id == line.RemittanceFileId, ct);
            var clp = JsonSerializer.Deserialize<ClaimPayment>(line.RawClpJson, EventEnvelope.Json)!;
            if (!await posting.PostAsync(file, clp, ct)) continue;

            line.ResolvedAt = clock.GetUtcNow();
            line.ResolvedBy = "auto-retry";
            line.Resolution = "Matched after the claim became known";
            file.MatchedCount++;
            file.UnmatchedCount--;
        }
        await db.SaveChangesAsync(ct);
    }
}
