using ClaimBridge.Contracts.Reference;
using ClaimBridge.PayerSim.Engine;
using ClaimBridge.X12.Transactions;

namespace ClaimBridge.PayerSim.Payers;

/// <summary>Shared adjudication path: decide, remember, and add reversals for corrected or voided claims.</summary>
public sealed class AdjudicationFlow(ClaimStore store, Adjudicator adjudicator, SimScenario scenario)
{
    public IReadOnlyList<ClaimPayment> Adjudicate(SimClaimRecord record)
    {
        var payments = new List<ClaimPayment>();
        var claim = record.Claim;

        // a replacement or void takes back whatever was paid on the original
        if (claim.FrequencyCode is "7" or "8" && claim.OriginalReference is { } original)
        {
            var prior = store.Find(claim.PayerId, original, original);
            if (prior?.Payment is { PaidAmount: > 0 } paid)
            {
                payments.Add(Adjudicator.Reversal(paid));
                prior.Payment = null;
            }
        }

        if (claim.FrequencyCode == "8")
        {
            record.State = SimClaimState.Finalized;
            record.Outcome = "denied";
            record.Note = "Void processed";
            return payments;
        }

        if (scenario.PendNext > 0)
        {
            scenario.PendNext--;
            record.State = SimClaimState.Pended;
            record.Note = "Pended for medical records";
            return payments;
        }

        var result = adjudicator.Adjudicate(claim, record.PayerClaimId);
        record.State = SimClaimState.Finalized;
        record.Outcome = result.Outcome;
        record.Note = result.Note;
        record.Payment = result.Payment;
        record.Paid = result.Payment.PaidAmount;
        payments.Add(result.Payment);
        return payments;
    }

    public static string Prefix(string payerId) => payerId switch
    {
        SeedIds.PayerA => "NW",
        SeedIds.PayerB => "BPK",
        _ => "CC"
    };
}
