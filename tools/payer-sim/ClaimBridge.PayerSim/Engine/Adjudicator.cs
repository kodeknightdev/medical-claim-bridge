using ClaimBridge.Contracts.Reference;
using ClaimBridge.X12.Transactions;

namespace ClaimBridge.PayerSim.Engine;

public sealed record AdjudicationResult(string Outcome, ClaimPayment Payment, string Note);

/// <summary>
/// Deterministic toy adjudication: fee schedule as a share of charge, deductible then copay/coinsurance,
/// and a few denial rules that match the scrubber's payer edits so demo scenarios are repeatable.
/// </summary>
public sealed class Adjudicator(MemberDirectory members, SimScenario scenario)
{
    private static readonly HashSet<string> NeedsPriorAuth = ["73721", "72148", "29881", "27447"];
    private static readonly HashSet<string> EmCodes = ["99202", "99203", "99204", "99205", "99212", "99213", "99214", "99215"];
    private static readonly HashSet<string> TherapyCodes = ["97110", "97140"];

    public static decimal FeeFactor(string payerId) => payerId switch
    {
        SeedIds.PayerA => 0.80m,
        SeedIds.PayerB => 0.75m,
        _ => 0.70m
    };

    public AdjudicationResult Adjudicate(SimClaim claim, string payerClaimId)
    {
        var member = members.Find(claim.MemberId);
        var firstDos = claim.Lines.Min(l => l.ServiceDate);

        if (member is null || !members.IsCovered(member, firstDos))
            return Deny(claim, payerClaimId, "27", ["N130"], "Coverage not active on the date of service");

        if (claim.Lines.Any(l => NeedsPriorAuth.Contains(l.ProcedureCode)) && string.IsNullOrWhiteSpace(claim.PriorAuthNumber))
            return Deny(claim, payerClaimId, "197", ["N54"], "Prior authorisation required");

        // a small, repeatable slice of claims gets a missing-information denial
        if (claim.FrequencyCode == "1" && Bucket(claim.ClaimNumber) < scenario.RandomDenialRate)
            return Deny(claim, payerClaimId, "16", ["M76"], "Missing or invalid information");

        var deductibleLeft = Math.Max(0, member.Deductible - member.DeductibleMet);
        var services = new List<ServicePayment>();
        decimal paid = 0, patient = 0;
        var anyLineDenied = false;

        foreach (var line in claim.Lines)
        {
            var adjustments = new List<Adjustment>();
            var remarks = new List<string>();

            if (claim.PayerId == SeedIds.PayerB && TherapyCodes.Contains(line.ProcedureCode) && line.Units > 4)
            {
                anyLineDenied = true;
                adjustments.Add(new Adjustment("CO", "151", line.Charge));
                remarks.Add("N362");
                services.Add(new ServicePayment(line.ProcedureCode, line.Modifiers, line.Charge, 0, line.Units, line.ServiceDate, 0, adjustments, remarks));
                continue;
            }
            if (claim.PayerId == SeedIds.PayerB && line.Modifiers.Contains("59"))
            {
                anyLineDenied = true;
                adjustments.Add(new Adjustment("CO", "4", line.Charge));
                services.Add(new ServicePayment(line.ProcedureCode, line.Modifiers, line.Charge, 0, line.Units, line.ServiceDate, 0, adjustments, remarks));
                continue;
            }

            var allowed = Math.Round(line.Charge * FeeFactor(claim.PayerId), 2);
            if (line.Charge > allowed) adjustments.Add(new Adjustment("CO", "45", line.Charge - allowed));

            // Northwind's PPO covers office visits with a copay before the deductible
            var copayOnly = claim.PayerId == SeedIds.PayerA && EmCodes.Contains(line.ProcedureCode);
            var lineDeductible = copayOnly ? 0 : Math.Min(deductibleLeft, allowed);
            deductibleLeft -= lineDeductible;
            var remaining = allowed - lineDeductible;

            decimal share;
            string shareReason;
            if (claim.PayerId == SeedIds.PayerA && EmCodes.Contains(line.ProcedureCode))
            {
                share = Math.Min(member.Copay, remaining);
                shareReason = "3";
            }
            else
            {
                share = Math.Round(remaining * 0.20m, 2);
                shareReason = "2";
            }

            if (lineDeductible > 0) adjustments.Add(new Adjustment("PR", "1", lineDeductible));
            if (share > 0) adjustments.Add(new Adjustment("PR", shareReason, share));

            var linePaid = remaining - share;
            paid += linePaid;
            patient += lineDeductible + share;
            services.Add(new ServicePayment(line.ProcedureCode, line.Modifiers, line.Charge, linePaid, line.Units, line.ServiceDate, allowed,
                adjustments, remarks));
        }

        var allDenied = services.All(s => s.Paid == 0 && s.Adjustments.All(a => a.GroupCode == "CO" && a.ReasonCode != "45"));
        if (allDenied) return Deny(claim, payerClaimId, services[0].Adjustments.FirstOrDefault()?.ReasonCode ?? "16", [], "All lines denied");

        var outcome = anyLineDenied ? "partial" : "paid";
        var payment = new ClaimPayment(claim.ClaimNumber, "1", claim.TotalCharge, paid, patient, payerClaimId, [], services, []);
        return new AdjudicationResult(outcome, payment, anyLineDenied ? "Some lines denied" : "Processed as primary");
    }

    public static ClaimPayment Reversal(ClaimPayment original) => original with
    {
        StatusCode = "22",
        ChargeAmount = -original.ChargeAmount,
        PaidAmount = -original.PaidAmount,
        PatientResponsibility = -original.PatientResponsibility,
        Services = [.. original.Services.Select(s => s with
        {
            Charge = -s.Charge,
            Paid = -s.Paid,
            Allowed = -s.Allowed,
            Adjustments = [.. s.Adjustments.Select(a => a with { Amount = -a.Amount })]
        })],
        ClaimAdjustments = [.. original.ClaimAdjustments.Select(a => a with { Amount = -a.Amount })]
    };

    private static AdjudicationResult Deny(SimClaim claim, string payerClaimId, string carc, string[] rarc, string note)
    {
        var services = claim.Lines.Select(l => new ServicePayment(l.ProcedureCode, l.Modifiers, l.Charge, 0, l.Units, l.ServiceDate, 0,
            [new Adjustment("CO", carc, l.Charge)], [])).ToList();
        var payment = new ClaimPayment(claim.ClaimNumber, "4", claim.TotalCharge, 0, 0, payerClaimId, [], services, rarc);
        return new AdjudicationResult("denied", payment, note);
    }

    private static double Bucket(string claimNumber) =>
        (uint)claimNumber.Aggregate(17, (h, c) => unchecked(h * 31 + c)) % 1000 / 1000.0;
}
