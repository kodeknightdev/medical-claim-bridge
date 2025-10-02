using ClaimBridge.PayersGateway.Consumers;
using ClaimBridge.PayersGateway.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace ClaimBridge.PayersGateway.Jobs;

/// <summary>Scheduled 276/277 inquiries for claims the payer accepted but has gone quiet on, or left pended.</summary>
[DisallowConcurrentExecution]
public class ClaimStatusInquiryJob(PayersDb db, StatusInquiryService inquiries, IConfiguration config, TimeProvider clock) : IJob
{
    private static readonly string?[] OpenStatuses = [null, "received", "pended", "in-process"];

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var now = clock.GetUtcNow();
        var silentAfter = now.AddMinutes(-config.GetValue("Inquiry:SilentAfterMinutes", 10));
        var every = now.AddMinutes(-config.GetValue("Inquiry:EveryMinutes", 10));

        var candidates = await db.Submissions
            .Where(s => s.Status == SubmissionStatus.Accepted
                        && OpenStatuses.Contains(s.LastPayerStatus)
                        && s.AckAt < silentAfter
                        && (s.LastInquiryAt == null || s.LastInquiryAt < every))
            .OrderBy(s => s.LastInquiryAt).Take(25).ToListAsync(ct);

        foreach (var submission in candidates)
            await inquiries.InquireAsync(submission, ct);
        if (candidates.Count > 0) await db.SaveChangesAsync(ct);
    }
}
