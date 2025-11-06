using ClaimBridge.Contracts.Submission;
using ClaimBridge.X12.Core;
using ClaimBridge.X12.Transactions;

namespace ClaimBridge.X12.Tests;

public class Claim837PSnapshotTests
{
    private static readonly DateOnly Dos = new(2025, 10, 20);
    private static readonly InterchangeOptions Options = new("CLAIMBRIDGE", "BLUEPEAK", 1042, new DateTimeOffset(2025, 10, 24, 14, 5, 0, TimeSpan.Zero));
    private static readonly SubmitterInfo Submitter = new("CLAIMBRIDGE", "Claim Bridge Clearinghouse", "edi@claimbridge.local");
    private static readonly ReceiverInfo Receiver = new("BLUEPEAK", "Bluepeak Mutual", "PAYER-B");

    private static readonly BillingProvider Sunrise = new("Sunrise Family Clinic", "1999000015", "00-0001001", "100 Demo Way", "Springfield", "IL", "62701");
    private static readonly BillingProvider Lakeside = new("Lakeside Orthopedics", "1999000023", "00-0001002", "200 Example Ave", "Lakeview", "WI", "53001");

    private static SubmissionDocument Claim(string number, BillingProvider billing, SubscriberInfo patient, string[] dx, SubmissionLine[] lines,
        string frequency = "1", string? original = null, string? priorAuth = null) =>
        new(Guid.Empty, number, 1, frequency, original, original is null ? null : "BPK" + original[^4..], Guid.Empty, "PAYER-B", billing,
            "1999000114", patient, priorAuth, dx, lines, lines.Sum(l => l.Charge));

    [Fact]
    public Task Office_visit_with_lab() => Verify(Claim837PWriter.Write(
    [
        Claim("CB250100120", Sunrise, new SubscriberInfo("BPK1000003", "Taylor", "Testwell", new DateOnly(1962, 3, 13), "M"), ["E11.9", "I10"],
        [
            new(1, "99214", ["25"], 1, 175, Dos, "11", [1, 2]),
            new(2, "36415", [], 1, 15, Dos, "11", [1]),
            new(3, "83036", [], 1, 40, Dos, "11", [1])
        ])
    ], Submitter, Receiver, Options, "CB1042"));

    [Fact]
    public Task Two_billing_providers_in_one_batch() => Verify(Claim837PWriter.Write(
    [
        Claim("CB250100121", Sunrise, new SubscriberInfo("BPK1000005", "Casey", "Mockford", new DateOnly(1970, 5, 15), "M"), ["M54.50"],
            [new(1, "97110", [], 2, 90, Dos, "11", [1])]),
        Claim("CB250100122", Lakeside, new SubscriberInfo("BPK1000007", "Drew", "Placeholder", new DateOnly(1978, 7, 17), "M"), ["M17.11"],
            [new(1, "73721", ["RT"], 1, 1350, Dos, "22", [1])], priorAuth: "PA778899")
    ], Submitter, Receiver, Options, "CB1042"));

    [Fact]
    public Task Replacement_claim_references_the_payer_claim_number() => Verify(Claim837PWriter.Write(
    [
        Claim("CB250100130", Sunrise, new SubscriberInfo("BPK1000003", "Taylor", "Testwell", new DateOnly(1962, 3, 13), "M"), ["I10"],
            [new(1, "99213", [], 1, 120, Dos, "11", [1])], frequency: "7", original: "CB250100120")
    ], Submitter, Receiver, Options, "CB1042"));

    [Fact]
    public void Written_837_reads_back_into_the_same_claims()
    {
        var text = Claim837PWriter.Write(
        [
            Claim("CB250100120", Sunrise, new SubscriberInfo("BPK1000003", "Taylor", "Testwell", new DateOnly(1962, 3, 13), "M"),
                ["E11.9", "I10"], [new(1, "99214", ["25"], 1, 175, Dos, "11", [1, 2]), new(2, "36415", [], 1, 15, Dos, "11", [1])],
                priorAuth: "PA1")
        ], Submitter, Receiver, Options, "CB1042");

        var claim = Claim837PReader.Read(text).Single();
        claim.ClaimNumber.ShouldBe("CB250100120");
        claim.Diagnoses.ShouldBe(["E11.9", "I10"]);
        claim.PriorAuthNumber.ShouldBe("PA1");
        claim.Subscriber.DateOfBirth.ShouldBe(new DateOnly(1962, 3, 13));
        claim.Lines[0].Modifiers.ShouldBe(["25"]);
        claim.Lines[0].DxPointers.ShouldBe([1, 2]);
        claim.Lines[1].ServiceDate.ShouldBe(Dos);
        claim.TotalCharge.ShouldBe(190);
    }

    [Fact]
    public void Delimiter_characters_in_names_are_stripped()
    {
        var text = Claim837PWriter.Write(
        [
            Claim("CB250100140", Sunrise with { Name = "Sunrise*Family~Clinic" },
                new SubscriberInfo("BPK1", "Ann:Marie", "O^Brien", new DateOnly(1990, 1, 1), "F"), ["I10"], [new(1, "99213", [], 1, 120, Dos, "11", [1])])
        ], Submitter, Receiver, Options, "CB1042");
        Should.NotThrow(() => X12Reader.Parse(text));
        text.ShouldContain("NM1*IL*1*OBRIEN*ANNMARIE");
    }
}
