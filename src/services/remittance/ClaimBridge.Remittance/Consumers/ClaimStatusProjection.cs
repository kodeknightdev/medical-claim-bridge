using ClaimBridge.Contracts.Events;
using ClaimBridge.Remittance.Data;
using ClaimBridge.ServiceDefaults.Messaging;

namespace ClaimBridge.Remittance.Consumers;

/// <summary>Keeps the list of expected claims (for ERA matching) and opens A/R balances on submission.</summary>
public class ClaimStatusProjection(RemittanceDb db, TimeProvider clock) : IEventHandler
{
    public async Task HandleAsync(EventEnvelope envelope, string topic, CancellationToken ct)
    {
        var e = envelope.DataAs<ClaimStatusChanged>();
        var now = clock.GetUtcNow();
        var claim = await db.ExpectedClaims.FindAsync([e.ClaimId], ct);
        if (claim is null)
        {
            claim = new ExpectedClaim { ClaimId = e.ClaimId, ClaimNumber = e.ClaimNumber, OrgId = e.OrgId, PayerId = e.PayerId };
            db.ExpectedClaims.Add(claim);
        }
        claim.TotalCharge = e.TotalCharge;
        claim.LastStatus = e.ToStatus;
        claim.SubmittedAt ??= e.SubmittedAt;
        claim.FirstServiceDate ??= e.FirstServiceDate;
        claim.UpdatedAt = now;

        var balance = await db.Balances.FindAsync([e.ClaimId], ct);
        if (balance is null && e.ToStatus == "Submitted")
        {
            balance = new ClaimBalance
            {
                ClaimId = e.ClaimId, ClaimNumber = e.ClaimNumber, OrgId = e.OrgId, PayerId = e.PayerId, Charge = e.TotalCharge,
                Status = BalanceStatus.Open, OpenedAt = e.SubmittedAt ?? now, FirstServiceDate = e.FirstServiceDate, UpdatedAt = now
            };
            balance.Recalculate();
            db.Balances.Add(balance);
        }

        // a corrected or voided claim hands its receivable to the replacement
        if (balance is not null && e.ToStatus is "Superseded" or "Voided" or "Closed")
        {
            if (e.ToStatus != "Closed" || balance.PayerOutstanding == 0)
            {
                balance.Status = BalanceStatus.Closed;
                balance.UpdatedAt = now;
                balance.Recalculate();
            }
        }
        if (balance is not null && e.ToStatus == "RejectedByPayer")
        {
            balance.Status = BalanceStatus.Closed;
            balance.Recalculate();
        }
    }
}
