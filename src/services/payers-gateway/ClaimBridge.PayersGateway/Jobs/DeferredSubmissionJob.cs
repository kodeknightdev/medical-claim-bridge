using ClaimBridge.PayersGateway.Consumers;
using ClaimBridge.PayersGateway.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace ClaimBridge.PayersGateway.Jobs;

/// <summary>Retries submissions parked while a payer was down (circuit open, timeouts, 5xx).</summary>
[DisallowConcurrentExecution]
public class DeferredSubmissionJob(PayersDb db, ClaimsClient claims, SubmissionService submissions, TimeProvider clock) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var now = clock.GetUtcNow();
        var due = await db.Submissions
            .Where(s => s.Status == SubmissionStatus.Deferred && s.NextAttemptAt <= now)
            .OrderBy(s => s.NextAttemptAt).Take(20).ToListAsync(ct);

        foreach (var submission in due)
        {
            var document = await claims.GetSubmissionAsync(submission.ClaimId, ct);
            await submissions.SubmitAsync(submission, document, ct);
            await db.SaveChangesAsync(ct);
        }
    }
}
