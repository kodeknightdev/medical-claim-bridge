using ClaimBridge.X12.Core;
using ClaimBridge.X12.Transactions;

namespace ClaimBridge.X12.Tests;

public class AcknowledgementAndRemittanceTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    [Fact]
    public void Accepted_999_reports_the_acknowledged_group()
    {
        var ack = Ack999.Read(Sample("ack.999.x12"));
        ack.Accepted.ShouldBeTrue();
        ack.AcknowledgedGroupControlNumber.ShouldBe("1042");
        ack.Sets.Single().Accepted.ShouldBeTrue();
    }

    [Fact]
    public void Rejected_999_carries_the_segment_errors()
    {
        var ack = Ack999.Read(Sample("ack-rejected.999.x12"));
        ack.Accepted.ShouldBeFalse();
        ack.Sets.Single().Errors.ShouldNotBeEmpty();
    }

    [Fact]
    public void Claim_level_277ca_splits_accepted_and_rejected_claims()
    {
        var claims = ClaimAck277CA.Read(Sample("claim-ack.277ca.x12")).Claims;
        claims.Count.ShouldBe(2);
        claims[0].ShouldSatisfyAllConditions(c => c.Accepted.ShouldBeTrue(), c => c.PayerClaimId.ShouldBe("BPK100120"));
        claims[1].ShouldSatisfyAllConditions(c => c.Accepted.ShouldBeFalse(), c => c.StatusCategory.ShouldBe("A7"),
            c => c.Message.ShouldBe("Subscriber not found"));
    }

    [Fact]
    public void Remittance_835_reads_payments_denials_reversals_and_remarks()
    {
        var era = RemittanceAdvice.Read(Sample("remittance.835.x12"));
        era.EftTraceNumber.ShouldBe("EFT2025110300017");
        era.PaymentDate.ShouldBe(new DateOnly(2025, 11, 3));
        era.Claims.Count.ShouldBe(4);

        var partial = era.Claims[0];
        partial.PaidAmount.ShouldBe(142.50m);
        partial.AllowedAmount.ShouldBe(172.5m);
        partial.AllAdjustments.ShouldContain(a => a.GroupCode == "CO" && a.ReasonCode == "97" && a.Amount == 15);
        partial.Services[1].RemarkCodes.ShouldBe(["M15"]);
        (partial.ChargeAmount - partial.PaidAmount - partial.AllAdjustments.Sum(a => a.Amount)).ShouldBe(0);

        era.Claims[1].IsDenied.ShouldBeTrue();
        era.Claims[1].RemarkCodes.ShouldBe(["N54"]);
        era.Claims[2].IsReversal.ShouldBeTrue();
        era.Claims[2].PaidAmount.ShouldBe(-120);
    }

    [Fact]
    public void Remittance_round_trips_through_the_writer()
    {
        var original = RemittanceAdvice.Read(Sample("remittance.835.x12"));
        var rewritten = RemittanceAdvice.Read(original.Write(new InterchangeOptions("BLUEPEAK", "CLAIMBRIDGE", 9, DateTimeOffset.UnixEpoch), "Sunrise"));
        rewritten.Claims.Select(c => (c.ClaimNumber, c.PaidAmount, c.StatusCode))
            .ShouldBe(original.Claims.Select(c => (c.ClaimNumber, c.PaidAmount, c.StatusCode)));
    }

    [Fact]
    public void Malformed_835_with_a_wrong_segment_count_is_rejected()
    {
        var ex = Should.Throw<X12ParseException>(() => RemittanceAdvice.Read(Sample("malformed.835.x12")));
        ex.Message.ShouldContain("SE segment count");
    }

    [Theory]
    [InlineData("", "File is empty")]
    [InlineData("GS*HC*A*B~", "ISA")]
    [InlineData("ISA*00*", "complete ISA")]
    public void Truncated_or_missing_envelopes_are_rejected(string text, string expected) =>
        Should.Throw<X12ParseException>(() => X12Reader.Parse(text)).Message.ShouldContain(expected);

    [Fact]
    public void Mismatched_control_numbers_are_rejected()
    {
        var text = Sample("ack.999.x12").Replace("IEA*1*000007001", "IEA*1*000007999");
        Should.Throw<X12ParseException>(() => X12Reader.Parse(text)).Message.ShouldContain("IEA control number");
    }

    [Fact]
    public void Wrong_transaction_type_is_rejected()
    {
        Should.Throw<X12ParseException>(() => RemittanceAdvice.Read(Sample("ack.999.x12"))).Message.ShouldContain("Expected an 835");
    }

    [Fact]
    public void File_kind_is_detected_from_st01()
    {
        X12FileKind.Detect(Sample("claim-ack.277ca.x12")).ShouldBe("277");
        X12FileKind.Detect(Sample("payer-b-batch.837.x12")).ShouldBe("837");
    }

    [Theory]
    [InlineData("M17.11", "M1711")]
    [InlineData("I10", "I10")]
    [InlineData("S83.241A", "S83241A")]
    public void Icd_codes_drop_the_dot_in_x12(string icd, string x12)
    {
        X12Values.IcdToX12(icd).ShouldBe(x12);
        X12Values.IcdFromX12(x12).ShouldBe(icd);
    }
}
