using ClaimBridge.Contracts.ReferenceData;
using ClaimBridge.X12.Transactions;

namespace ClaimBridge.Remittance.Domain;

public enum PostingKind { Payment, PartialPayment, Denial, Reversal }

public sealed record PostingResult(
    PostingKind Kind,
    decimal Charge,
    decimal Paid,
    decimal Allowed,
    decimal PatientResponsibility,
    decimal ContractualAdjustments,
    decimal OtherAdjustments,
    IReadOnlyList<Adjustment> Adjustments,
    IReadOnlyList<string> RemarkCodes,
    string? PrimaryDenialCarc,
    string? DenialCategory,
    decimal Imbalance)
{
    public bool Balanced => Imbalance == 0;
}

/// <summary>
/// Turns one 835 CLP loop into posting amounts. The invariant every posted claim has to meet:
/// charge = paid + CO/OA adjustments + patient responsibility (PR).
/// </summary>
public static class PostingCalculator
{
    public static PostingResult Calculate(ClaimPayment clp)
    {
        var adjustments = clp.AllAdjustments;
        var pr = adjustments.Where(a => a.GroupCode == "PR").Sum(a => a.Amount);
        var co = adjustments.Where(a => a.GroupCode is "CO" or "PI").Sum(a => a.Amount);
        var oa = adjustments.Where(a => a.GroupCode == "OA").Sum(a => a.Amount);

        // CLP05 should equal the PR adjustments; when a payer only sends one of them, trust CLP05
        var patient = clp.PatientResponsibility != 0 ? clp.PatientResponsibility : pr;
        var allowed = clp.AllowedAmount ?? clp.PaidAmount + patient;
        var imbalance = clp.ChargeAmount - (clp.PaidAmount + co + oa + patient);

        var denialAdjustment = adjustments
            .Where(a => a.GroupCode is "CO" or "OA" && a.ReasonCode is not ("45" or "253") && a.Amount > 0)
            .OrderByDescending(a => a.Amount)
            .FirstOrDefault();

        var kind = clp switch
        {
            { IsReversal: true } => PostingKind.Reversal,
            { IsDenied: true } => PostingKind.Denial,
            { PaidAmount: 0 } when patient == 0 => PostingKind.Denial,
            _ when denialAdjustment is not null => PostingKind.PartialPayment,
            _ => PostingKind.Payment
        };

        var carc = kind is PostingKind.Denial or PostingKind.PartialPayment ? denialAdjustment?.ReasonCode : null;
        return new PostingResult(kind, clp.ChargeAmount, clp.PaidAmount, allowed, patient, co, oa, adjustments,
            [.. clp.RemarkCodes, .. clp.Services.SelectMany(s => s.RemarkCodes)],
            carc, carc is null ? null : AdjustmentReasons.CategoryFor(carc), Math.Round(imbalance, 2));
    }
}
