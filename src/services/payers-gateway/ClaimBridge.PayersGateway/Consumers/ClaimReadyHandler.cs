using ClaimBridge.Contracts.Events;
using ClaimBridge.PayersGateway.Adapters;
using ClaimBridge.PayersGateway.Data;
using ClaimBridge.ServiceDefaults.Messaging;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.PayersGateway.Consumers;

public class ClaimReadyHandler(
    PayersDb db,
    ClaimsClient claims,
    PayerAdapterRegistry adapters,
    SubmissionService submissions,
    TimeProvider clock) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        var ready = envelope.DataAs<ClaimReady>();
        if (await db.Submissions.AnyAsync(s => s.ClaimId == ready.ClaimId && s.Attempt == ready.Attempt, ct)) return;

        var document = await claims.GetSubmissionAsync(ready.ClaimId, ct);
        var submission = new Submission
        {
            Id = Guid.NewGuid(),
            ClaimId = ready.ClaimId,
            ClaimNumber = ready.ClaimNumber,
            OrgId = ready.OrgId,
            PayerId = ready.PayerId,
            Attempt = ready.Attempt,
            Channel = adapters.For(ready.PayerId).Channel,
            Status = SubmissionStatus.Queued,
            CreatedAt = clock.GetUtcNow()
        };
        db.Submissions.Add(submission);
        await submissions.SubmitAsync(submission, document, ct);
    }
}
