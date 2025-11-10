using ClaimBridge.Remittance.Domain;
using ClaimBridge.X12.Transactions;

namespace ClaimBridge.Remittance.Tests;

public class PostingCalculatorTests
{
    private static ClaimPayment Clp(string status, decimal charge, decimal paid, decimal pr, params Adjustment[] adjustments) =>
        new("CB250000001", status, charge, paid, pr, "P1", adjustments, [], []);

    [Fact]
    public void Full_payment_balances_charge_against_paid_contractual_and_patient_share()
    {
        var r = PostingCalculator.Calculate(Clp("1", 200, 120, 40, new Adjustment("CO", "45", 40), new Adjustment("PR", "2", 40)));
        r.Kind.ShouldBe(PostingKind.Payment);
        r.Balanced.ShouldBeTrue();
        (r.Paid + r.ContractualAdjustments + r.OtherAdjustments + r.PatientResponsibility).ShouldBe(r.Charge);
        r.Allowed.ShouldBe(160);
    }

    [Fact]
    public void A_denied_line_inside_a_paid_claim_is_a_partial_payment()
    {
        var r = PostingCalculator.Calculate(Clp("1", 230, 142.5m, 30, new Adjustment("CO", "45", 42.5m), new Adjustment("PR", "2", 30), new Adjustment("CO", "97", 15)));
        r.Kind.ShouldBe(PostingKind.PartialPayment);
        r.PrimaryDenialCarc.ShouldBe("97");
        r.DenialCategory.ShouldBe("Bundling");
        r.Balanced.ShouldBeTrue();
    }

    [Fact]
    public void Status_4_or_nothing_paid_and_nothing_owed_is_a_denial()
    {
        PostingCalculator.Calculate(Clp("4", 1350, 0, 0, new Adjustment("CO", "197", 1350))).ShouldSatisfyAllConditions(
            r => r.Kind.ShouldBe(PostingKind.Denial), r => r.PrimaryDenialCarc.ShouldBe("197"), r => r.DenialCategory.ShouldBe("Authorization"));
        PostingCalculator.Calculate(Clp("1", 100, 0, 0, new Adjustment("CO", "50", 100))).Kind.ShouldBe(PostingKind.Denial);
    }

    [Fact]
    public void Everything_applied_to_the_deductible_is_still_a_payment()
    {
        var r = PostingCalculator.Calculate(Clp("1", 120, 0, 96, new Adjustment("CO", "45", 24), new Adjustment("PR", "1", 96)));
        r.Kind.ShouldBe(PostingKind.Payment);
        r.PatientResponsibility.ShouldBe(96);
    }

    [Fact]
    public void Takeback_carries_negative_amounts_and_still_balances()
    {
        var r = PostingCalculator.Calculate(Clp("22", -180, -120, -24, new Adjustment("CO", "45", -36), new Adjustment("PR", "2", -24)));
        r.Kind.ShouldBe(PostingKind.Reversal);
        r.Balanced.ShouldBeTrue();
    }

    [Fact]
    public void Unbalanced_lines_report_the_difference()
    {
        var r = PostingCalculator.Calculate(Clp("1", 200, 150, 0, new Adjustment("CO", "45", 40)));
        r.Balanced.ShouldBeFalse();
        r.Imbalance.ShouldBe(10);
    }

    [Fact]
    public void Patient_responsibility_falls_back_to_pr_adjustments_when_clp05_is_empty()
    {
        PostingCalculator.Calculate(Clp("1", 100, 70, 0, new Adjustment("CO", "45", 10), new Adjustment("PR", "3", 20))).PatientResponsibility.ShouldBe(20);
    }
}
