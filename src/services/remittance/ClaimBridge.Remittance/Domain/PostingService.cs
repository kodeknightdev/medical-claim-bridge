using System.Text.Json;
using ClaimBridge.Contracts.Events;
using ClaimBridge.Remittance.Data;
using ClaimBridge.ServiceDefaults.Messaging;
using ClaimBridge.X12.Transactions;
using Microsoft.EntityFrameworkCore;

namespace ClaimBridge.Remittance.Domain;

public class PostingService(RemittanceDb db, IEventOutbox outbox, RemittanceMetrics metrics, TimeProvider clock)
{
    /// <summary>Posts one CLP loop, or parks it in the reconciliation queue. Never throws for business mismatches.</summary>
    public async Task<bool> PostAsync(RemittanceFile file, ClaimPayment clp, CancellationToken ct)
    {
        var expected = await db.ExpectedClaims.FirstOrDefaultAsync(c => c.ClaimNumber == clp.ClaimNumber && c.PayerId == file.PayerId, ct);
        if (expected is null)
        {
            Park(file, clp, "No submitted claim with this claim control number for the payer");
            return false;
        }

        var result = PostingCalculator.Calculate(clp);
        if (!result.Balanced)
        {
            Park(file, clp, $"Does not balance: charge {result.Charge:0.00} vs paid + adjustments + PR off by {result.Imbalance:0.00}");
            return false;
        }

        var now = clock.GetUtcNow();
        var posting = new Posting
        {
            Id = Guid.NewGuid(), RemittanceFileId = file.Id, ClaimId = expected.ClaimId, ClaimNumber = expected.ClaimNumber,
            OrgId = expected.OrgId, PayerId = file.PayerId, Kind = result.Kind.ToString(), ClpStatus = clp.StatusCode,
            PayerClaimId = clp.PayerClaimId, Charge = result.Charge, Paid = result.Paid, Allowed = result.Allowed,
            PatientResponsibility = result.PatientResponsibility, ContractualAdjustments = result.ContractualAdjustments,
            OtherAdjustments = result.OtherAdjustments, DenialCarc = result.PrimaryDenialCarc, DenialCategory = result.DenialCategory,
            RemarkCodes = [.. result.RemarkCodes.Distinct()], EftTraceNumber = file.EftTraceNumber, PaymentDate = file.PaymentDate,
            PostedAt = now,
            Adjustments = [.. result.Adjustments.Select(a => new PostingAdjustment { GroupCode = a.GroupCode, ReasonCode = a.ReasonCode, Amount = a.Amount })]
        };
        db.Postings.Add(posting);

        var balance = await db.Balances.FindAsync([expected.ClaimId], ct) ?? OpenBalance(expected, now);
        ApplyToBalance(balance, result, now);
        Publish(expected, file, posting, result);
        metrics.Posted(file.PayerId, result.Kind, result.Paid);
        return true;
    }

    public static void ApplyToBalance(ClaimBalance balance, PostingResult result, DateTimeOffset now)
    {
        // reversal amounts arrive negative, so the same arithmetic undoes the original posting
        balance.Paid += result.Paid;
        balance.Adjusted += result.ContractualAdjustments + result.OtherAdjustments;
        balance.PatientResponsibility += result.PatientResponsibility;
        balance.Status = result.Kind switch
        {
            PostingKind.Reversal => BalanceStatus.Open,
            PostingKind.Denial => BalanceStatus.Denied,
            _ => BalanceStatus.Paid
        };
        if (result.Kind != PostingKind.Reversal) balance.LastPaymentAt = now;
        balance.UpdatedAt = now;
        balance.Recalculate();
    }

    private ClaimBalance OpenBalance(ExpectedClaim expected, DateTimeOffset now)
    {
        var balance = new ClaimBalance
        {
            ClaimId = expected.ClaimId, ClaimNumber = expected.ClaimNumber, OrgId = expected.OrgId, PayerId = expected.PayerId,
            Charge = expected.TotalCharge, Status = BalanceStatus.Open, OpenedAt = expected.SubmittedAt ?? now,
            FirstServiceDate = expected.FirstServiceDate, UpdatedAt = now
        };
        balance.Recalculate();
        db.Balances.Add(balance);
        return balance;
    }

    private void Publish(ExpectedClaim claim, RemittanceFile file, Posting posting, PostingResult result)
    {
        var key = claim.ClaimId.ToString();
        switch (result.Kind)
        {
            case PostingKind.Reversal:
                outbox.Enqueue(Topics.ReversalPosted, key, new ReversalPosted(claim.ClaimId, claim.ClaimNumber, claim.OrgId, claim.PayerId,
                    file.Id, -result.Paid, file.EftTraceNumber));
                break;
            case PostingKind.Denial:
                outbox.Enqueue(Topics.ClaimDenied, key, new ClaimDenied(claim.ClaimId, claim.ClaimNumber, claim.OrgId, claim.PayerId, file.Id,
                    result.Charge, result.PrimaryDenialCarc ?? "16", result.DenialCategory ?? "Other", result.RemarkCodes, file.PaymentDate));
                break;
            default:
                outbox.Enqueue(Topics.PaymentPosted, key, new PaymentPosted(claim.ClaimId, claim.ClaimNumber, claim.OrgId, claim.PayerId, file.Id,
                    result.Charge, result.Paid, result.Allowed, result.PatientResponsibility,
                    result.Adjustments.Select(a => new AdjustmentSummary(a.GroupCode, a.ReasonCode, a.Amount)).ToList(),
                    result.Kind == PostingKind.PartialPayment, file.EftTraceNumber, file.PaymentDate));
                break;
        }
    }

    private void Park(RemittanceFile file, ClaimPayment clp, string reason)
    {
        db.Unmatched.Add(new UnmatchedLine
        {
            Id = Guid.NewGuid(), RemittanceFileId = file.Id, PayerId = file.PayerId, ClaimNumber = clp.ClaimNumber,
            PayerClaimId = clp.PayerClaimId, Charge = clp.ChargeAmount, Paid = clp.PaidAmount, Reason = reason,
            RawClpJson = JsonSerializer.Serialize(clp, EventEnvelope.Json), CreatedAt = clock.GetUtcNow()
        });
        metrics.Unmatched(file.PayerId);
    }
}
