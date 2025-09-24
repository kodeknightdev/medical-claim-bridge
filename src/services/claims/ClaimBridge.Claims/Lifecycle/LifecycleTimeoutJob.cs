using ClaimBridge.Claims.Application;
using ClaimBridge.Claims.Application.Clients;
using ClaimBridge.Claims.Data;
using ClaimBridge.Claims.Domain;
using ClaimBridge.Contracts.Events;
using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Quartz;

namespace ClaimBridge.Claims.Lifecycle;

/// <summary>Fires expired durable deadlines: status inquiry, then one resubmission, then escalation to ops.</summary>
[DisallowConcurrentExecution]
public class LifecycleTimeoutJob(
    ClaimsDb db,
    ClaimTransitions transitions,
    StatusInquiryClient inquiries,
    IEventOutbox outbox,
    IOptions<LifecycleOptions> options,
    TimeProvider clock,
    ILogger<LifecycleTimeoutJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var now = clock.GetUtcNow();
        var due = await db.Lifecycles.Where(l => l.DeadlineAt != null && l.DeadlineAt <= now)
            .OrderBy(l => l.DeadlineAt).Take(50).ToListAsync(ct);

        foreach (var state in due)
        {
            var claim = await db.Claims.Include(c => c.History).FirstAsync(c => c.Id == state.ClaimId, ct);
            var action = LifecycleTimeoutPolicy.Decide(state, claim.Status, options.Value.MaxResubmissions);
            logger.LogInformation("Deadline {Kind} expired for {ClaimNumber}: {Action}", state.DeadlineKind, claim.ClaimNumber, action);

            switch (action)
            {
                case TimeoutAction.SendStatusInquiry:
                    var sent = await inquiries.RequestAsync(claim.Id, claim.ClaimNumber, claim.PayerId, ct);
                    state.InquiriesSent++;
                    claim.Note("StatusInquirySent", now, "process-manager",
                        sent ? "No acknowledgement within the payer SLA; 276 status inquiry sent" : "Status inquiry could not be sent");
                    state.SetDeadline(DeadlineKind.StatusInquiry, now.AddMinutes(options.Value.InquiryWaitMinutes), now);
                    break;

                case TimeoutAction.Resubmit:
                    state.Resubmissions++;
                    claim.Note("Resubmitted", now, "process-manager", "Still no acknowledgement after the status inquiry; resubmitting once");
                    outbox.Enqueue(Topics.ClaimReady, claim.Id.ToString(),
                        new ClaimReady(claim.Id, claim.ClaimNumber, claim.OrgId, claim.PayerId, state.Resubmissions + 1));
                    state.SetDeadline(DeadlineKind.Resubmission, now + options.Value.AckSla(claim.PayerId), now);
                    break;

                case TimeoutAction.Escalate:
                    transitions.Fire(claim, LifecycleTrigger.Escalate, "process-manager",
                        "No acknowledgement after a status inquiry and a resubmission; needs ops follow-up");
                    state.EscalatedAt = now;
                    state.ClearDeadline(now);
                    break;

                case TimeoutAction.Clear:
                    state.ClearDeadline(now);
                    break;
            }
            await transitions.ProjectAsync(claim, ct);
        }

        if (due.Count > 0) await db.SaveChangesAsync(ct);

        // idempotency keys only need to outlive client retries
        await db.IdempotencyRecords.Where(r => r.CreatedAt < now.AddDays(-1)).ExecuteDeleteAsync(ct);
    }
}
