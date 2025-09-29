using ClaimBridge.X12.Core;

namespace ClaimBridge.X12.Transactions;

/// <summary>Claim-level acknowledgement from a 277CA STC segment.</summary>
public sealed record ClaimAcknowledgement(
    string ClaimNumber,
    bool Accepted,
    string StatusCategory,
    string StatusCode,
    string? PayerClaimId,
    decimal ChargeAmount,
    string? Message);

public sealed record ClaimAck277CA(IReadOnlyList<ClaimAcknowledgement> Claims)
{
    public const string Version = "005010X214";

    // A1/A2 = accepted into adjudication, A3/A6/A7/A8 = rejected at the front end
    public static bool IsAccepted(string category) => category is "A1" or "A2" or "A5";

    public static ClaimAck277CA Read(string text)
    {
        var interchange = X12Reader.Parse(text);
        if (interchange.TransactionType != "277")
            throw new X12ParseException($"Expected a 277CA, found {interchange.TransactionType}");

        var d = interchange.Delimiters;
        var claims = new List<ClaimAcknowledgement>();
        string? trn = null;
        string? category = null, code = null, message = null, payerClaimId = null;
        decimal charge = 0;

        foreach (var s in interchange.Transaction("277"))
        {
            switch (s.Id)
            {
                case "HL":
                    Flush();
                    break;
                case "TRN" when s[1] == "2":
                    Flush();
                    trn = s[2];
                    break;
                case "STC" when trn is not null:
                    var status = s.Components(1, d.Component);
                    category = status.ElementAtOrDefault(0);
                    code = status.ElementAtOrDefault(1);
                    charge = X12Values.Amount(s[4]);
                    message = string.IsNullOrEmpty(s[12]) ? null : s[12];
                    break;
                case "REF" when trn is not null && s[1] == "1K":
                    payerClaimId = s[2];
                    break;
            }
        }
        Flush();
        return new ClaimAck277CA(claims);

        void Flush()
        {
            if (trn is null) return;
            if (category is null) throw new X12ParseException($"Claim {trn} in 277CA has no STC status");
            claims.Add(new ClaimAcknowledgement(trn, IsAccepted(category), category, code ?? "", payerClaimId, charge, message));
            trn = null;
            category = code = message = payerClaimId = null;
            charge = 0;
        }
    }

    public string Write(InterchangeOptions options, string payerName, string submitterName)
    {
        var w = new X12Writer(options, "HN", "277", Version);
        var today = X12Writer.Date(DateOnly.FromDateTime(options.CreatedAt.UtcDateTime));
        w.Add("BHT", "0085", "08", "ACK" + options.ControlNumber, today, options.CreatedAt.ToString("HHmm"), "TH");
        w.Add("HL", "1", "", "20", "1").Add("NM1", "PR", "2", X12Writer.Upper(payerName), "", "", "", "", "PI", options.SenderId);
        w.Add("HL", "2", "1", "21", "1").Add("NM1", "41", "2", X12Writer.Upper(submitterName), "", "", "", "", "46", options.ReceiverId);
        var hl = 2;
        foreach (var c in Claims)
        {
            w.Add("HL", (++hl).ToString(), "2", "PT");
            w.Add("TRN", "2", c.ClaimNumber);
            w.Add("STC", w.Composite(c.StatusCategory, c.StatusCode), today, c.Accepted ? "WQ" : "U", X12Writer.Amount(c.ChargeAmount),
                "", "", "", "", "", "", "", c.Message is null ? null : X12Writer.Clean(c.Message));
            if (c.PayerClaimId is not null) w.Add("REF", "1K", c.PayerClaimId);
        }
        return w.Build();
    }
}
