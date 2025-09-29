using ClaimBridge.Contracts.Submission;
using ClaimBridge.X12.Core;

namespace ClaimBridge.X12.Transactions;

public sealed record SubmitterInfo(string Id, string Name, string ContactEmail);
public sealed record ReceiverInfo(string Id, string Name, string PayerId);

/// <summary>Writes a simplified 005010X222A1 professional claim batch: one ST, billing providers and claims as HL loops.</summary>
public static class Claim837PWriter
{
    public const string Version = "005010X222A1";

    public static string Write(IReadOnlyList<SubmissionDocument> claims, SubmitterInfo submitter, ReceiverInfo receiver,
        InterchangeOptions options, string batchReference)
    {
        if (claims.Count == 0) throw new ArgumentException("At least one claim is required", nameof(claims));

        var w = new X12Writer(options, "HC", "837", Version);
        w.Add("BHT", "0019", "00", batchReference, X12Writer.Date(DateOnly.FromDateTime(options.CreatedAt.UtcDateTime)),
            options.CreatedAt.ToString("HHmm"), "CH");
        w.Add("NM1", "41", "2", X12Writer.Upper(submitter.Name), "", "", "", "", "46", submitter.Id);
        w.Add("PER", "IC", "EDI DESK", "EM", submitter.ContactEmail);
        w.Add("NM1", "40", "2", X12Writer.Upper(receiver.Name), "", "", "", "", "46", receiver.Id);

        var hl = 0;
        foreach (var provider in claims.GroupBy(c => c.Billing.Npi))
        {
            var billing = provider.First().Billing;
            var providerHl = ++hl;
            w.Add("HL", providerHl.ToString(), "", "20", "1");
            w.Add("NM1", "85", "2", X12Writer.Upper(billing.Name), "", "", "", "", "XX", billing.Npi);
            w.Add("N3", X12Writer.Upper(billing.AddressLine1));
            w.Add("N4", X12Writer.Upper(billing.City), billing.State, billing.Zip);
            w.Add("REF", "EI", billing.TaxId.Replace("-", ""));

            foreach (var claim in provider)
            {
                w.Add("HL", (++hl).ToString(), providerHl.ToString(), "22", "0");
                w.Add("SBR", "P", "18", "", "", "", "", "", "", "CI");
                var s = claim.Subscriber;
                w.Add("NM1", "IL", "1", X12Writer.Upper(s.LastName), X12Writer.Upper(s.FirstName), "", "", "", "MI", s.MemberId);
                w.Add("DMG", "D8", X12Writer.Date(s.DateOfBirth), s.Gender);
                w.Add("NM1", "PR", "2", X12Writer.Upper(receiver.Name), "", "", "", "", "PI", receiver.PayerId);

                var pos = claim.Lines[0].PlaceOfService;
                w.Add("CLM", claim.ClaimNumber, X12Writer.Amount(claim.TotalCharge), "", "", w.Composite(pos, "B", claim.FrequencyCode),
                    "Y", "A", "Y", "Y");
                if (claim.FrequencyCode is "7" or "8")
                    w.Add("REF", "F8", claim.OriginalPayerClaimId ?? claim.OriginalClaimNumber);
                if (!string.IsNullOrWhiteSpace(claim.PriorAuthNumber))
                    w.Add("REF", "G1", claim.PriorAuthNumber);

                // first code is the principal diagnosis (ABK), the rest are secondary (ABF)
                var hi = claim.Diagnoses.Select((d, i) => w.Composite(i == 0 ? "ABK" : "ABF", X12Values.IcdToX12(d))).ToArray();
                w.Add("HI", hi);
                if (!string.IsNullOrWhiteSpace(claim.RenderingNpi))
                    w.Add("NM1", "82", "1", "RENDERING", "PROVIDER", "", "", "", "XX", claim.RenderingNpi);

                foreach (var line in claim.Lines)
                {
                    w.Add("LX", line.LineNumber.ToString());
                    var procedure = w.Composite(["HC", line.ProcedureCode, .. line.Modifiers.Take(4)]);
                    w.Add("SV1", procedure, X12Writer.Amount(line.Charge), "UN", X12Writer.Amount(line.Units), line.PlaceOfService, "",
                        string.Join(':', line.DxPointers.Take(4)));
                    w.Add("DTP", "472", "D8", X12Writer.Date(line.ServiceDate));
                }
            }
        }
        return w.Build();
    }
}

public sealed record Parsed837Claim(
    string ClaimNumber,
    string FrequencyCode,
    string? OriginalReference,
    string BillingNpi,
    string BillingName,
    string? RenderingNpi,
    SubscriberInfo Subscriber,
    string PayerId,
    string? PriorAuthNumber,
    IReadOnlyList<string> Diagnoses,
    IReadOnlyList<SubmissionLine> Lines,
    decimal TotalCharge);

public static class Claim837PReader
{
    public static IReadOnlyList<Parsed837Claim> Read(string text)
    {
        var interchange = X12Reader.Parse(text);
        if (interchange.TransactionType != "837")
            throw new X12ParseException($"Expected an 837 transaction, found {interchange.TransactionType}");

        var d = interchange.Delimiters;
        var claims = new List<Parsed837Claim>();
        string billingNpi = "", billingName = "";
        SubscriberInfo? subscriber = null;
        string payerId = "";
        ClaimBuilder? current = null;

        foreach (var s in interchange.Transaction("837"))
        {
            switch (s.Id)
            {
                case "HL" when s[3] == "20":
                    Flush();
                    break;
                case "HL" when s[3] == "22":
                    Flush();
                    subscriber = null;
                    break;
                case "NM1" when s[1] == "85":
                    billingName = s[3];
                    billingNpi = s[9];
                    break;
                case "NM1" when s[1] == "IL":
                    subscriber = new SubscriberInfo(s[9], s[4], s[3], DateOnly.MinValue, "U");
                    break;
                case "DMG" when subscriber is not null:
                    subscriber = subscriber with { DateOfBirth = X12Values.Date(s[2]) ?? DateOnly.MinValue, Gender = s[3] };
                    break;
                case "NM1" when s[1] == "PR":
                    payerId = s[9];
                    break;
                case "CLM":
                    Flush();
                    if (subscriber is null) throw new X12ParseException("CLM found before subscriber NM1*IL", s.Index);
                    var clm05 = s.Components(5, d.Component);
                    current = new ClaimBuilder(s[1], clm05.ElementAtOrDefault(2) ?? "1", clm05.ElementAtOrDefault(0) ?? "11",
                        X12Values.Amount(s[2]), billingNpi, billingName, subscriber, payerId);
                    break;
                case "REF" when current is not null && s[1] == "F8":
                    current.OriginalReference = s[2];
                    break;
                case "REF" when current is not null && s[1] == "G1":
                    current.PriorAuth = s[2];
                    break;
                case "HI" when current is not null:
                    for (var i = 1; i < s.Elements.Count; i++)
                    {
                        var parts = s.Components(i, d.Component);
                        if (parts.Length >= 2) current.Diagnoses.Add(X12Values.IcdFromX12(parts[1]));
                    }
                    break;
                case "NM1" when current is not null && s[1] == "82":
                    current.RenderingNpi = s[9];
                    break;
                case "SV1" when current is not null:
                    var proc = s.Components(1, d.Component);
                    if (proc.Length < 2) throw new X12ParseException("SV1 is missing the procedure code", s.Index);
                    current.Lines.Add(new SubmissionLine(current.Lines.Count + 1, proc[1], proc.Skip(2).ToList(),
                        X12Values.Amount(s[4]), X12Values.Amount(s[2]), DateOnly.MinValue,
                        string.IsNullOrEmpty(s[5]) ? current.Pos : s[5],
                        s.Components(7, d.Component).Select(int.Parse).ToList()));
                    break;
                case "DTP" when current is not null && s[1] == "472" && current.Lines.Count > 0:
                    var last = current.Lines[^1];
                    current.Lines[^1] = last with { ServiceDate = X12Values.Date(s[3]) ?? DateOnly.MinValue };
                    break;
            }
        }
        Flush();
        return claims;

        void Flush()
        {
            if (current is null) return;
            if (current.Lines.Count == 0) throw new X12ParseException($"Claim {current.ClaimNumber} has no service lines");
            claims.Add(current.Build());
            current = null;
        }
    }

    private sealed class ClaimBuilder(string claimNumber, string frequency, string pos, decimal total, string billingNpi,
        string billingName, SubscriberInfo subscriber, string payerId)
    {
        public string ClaimNumber { get; } = claimNumber;
        public string Pos { get; } = pos;
        public string? OriginalReference { get; set; }
        public string? PriorAuth { get; set; }
        public string? RenderingNpi { get; set; }
        public List<string> Diagnoses { get; } = [];
        public List<SubmissionLine> Lines { get; } = [];

        public Parsed837Claim Build() => new(ClaimNumber, frequency, OriginalReference, billingNpi, billingName, RenderingNpi,
            subscriber, payerId, PriorAuth, Diagnoses, Lines, total);
    }
}
