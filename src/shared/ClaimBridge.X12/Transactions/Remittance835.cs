using ClaimBridge.X12.Core;

namespace ClaimBridge.X12.Transactions;

public sealed record Adjustment(string GroupCode, string ReasonCode, decimal Amount);

public sealed record ServicePayment(string ProcedureCode, IReadOnlyList<string> Modifiers, decimal Charge, decimal Paid, decimal Units,
    DateOnly? ServiceDate, decimal? Allowed, IReadOnlyList<Adjustment> Adjustments, IReadOnlyList<string> RemarkCodes);

/// <summary>One CLP loop. Status 1/2/3 = processed, 4 = denied, 22 = reversal of a previous payment.</summary>
public sealed record ClaimPayment(
    string ClaimNumber,
    string StatusCode,
    decimal ChargeAmount,
    decimal PaidAmount,
    decimal PatientResponsibility,
    string? PayerClaimId,
    IReadOnlyList<Adjustment> ClaimAdjustments,
    IReadOnlyList<ServicePayment> Services,
    IReadOnlyList<string> RemarkCodes)
{
    public bool IsReversal => StatusCode == "22";
    public bool IsDenied => StatusCode == "4";

    /// <summary>Claim-level plus service-level adjustments; the 835 allows either placement.</summary>
    public IReadOnlyList<Adjustment> AllAdjustments => [.. ClaimAdjustments, .. Services.SelectMany(s => s.Adjustments)];

    public decimal? AllowedAmount => Services.Any(s => s.Allowed is not null) ? Services.Sum(s => s.Allowed ?? 0) : null;
}

public sealed record RemittanceAdvice(
    string PayerName,
    string PayeeNpi,
    string? EftTraceNumber,
    DateOnly? PaymentDate,
    decimal TotalPaid,
    string PaymentMethod,
    IReadOnlyList<ClaimPayment> Claims)
{
    public const string Version = "005010X221A1";

    public static RemittanceAdvice Read(string text)
    {
        var interchange = X12Reader.Parse(text);
        if (interchange.TransactionType != "835")
            throw new X12ParseException($"Expected an 835, found {interchange.TransactionType}");

        var d = interchange.Delimiters;
        string payer = "", payeeNpi = "", method = "", trace = "";
        decimal total = 0;
        DateOnly? paidOn = null;
        var claims = new List<ClaimPayment>();
        ClaimState? claim = null;
        ServiceState? service = null;

        foreach (var s in interchange.Transaction("835"))
        {
            switch (s.Id)
            {
                case "BPR":
                    total = X12Values.Amount(s[2]);
                    method = s[4];
                    paidOn = X12Values.Date(s[16]);
                    break;
                case "TRN" when s[1] == "1":
                    trace = s[2];
                    break;
                case "N1" when s[1] == "PR":
                    payer = s[2];
                    break;
                case "N1" when s[1] == "PE":
                    payeeNpi = s[4];
                    break;
                case "CLP":
                    FlushClaim();
                    if (s[1].Length == 0) throw new X12ParseException("CLP is missing the claim control number", s.Index);
                    claim = new ClaimState(s[1], s[2], X12Values.Amount(s[3]), X12Values.Amount(s[4]), X12Values.Amount(s[5]),
                        string.IsNullOrEmpty(s[7]) ? null : s[7]);
                    break;
                case "CAS" when claim is not null:
                    var adjustments = ReadCas(s);
                    if (service is not null) service.Adjustments.AddRange(adjustments);
                    else claim.Adjustments.AddRange(adjustments);
                    break;
                case "SVC" when claim is not null:
                    FlushService();
                    var proc = s.Components(1, d.Component);
                    service = new ServiceState(proc.ElementAtOrDefault(1) ?? "", proc.Skip(2).ToList(), X12Values.Amount(s[2]),
                        X12Values.Amount(s[3]), string.IsNullOrEmpty(s[5]) ? 1 : X12Values.Amount(s[5]));
                    break;
                case "DTM" when service is not null && s[1] == "472":
                    service.ServiceDate = X12Values.Date(s[2]);
                    break;
                case "AMT" when service is not null && s[1] == "B6":
                    service.Allowed = X12Values.Amount(s[2]);
                    break;
                case "LQ" when s[1] == "HE":
                    if (service is not null) service.Remarks.Add(s[2]);
                    else claim?.Remarks.Add(s[2]);
                    break;
                case "MOA" or "MIA" when claim is not null:
                    for (var i = 1; i < s.Elements.Count; i++)
                        if (s[i].Length > 0 && char.IsLetter(s[i][0])) claim.Remarks.Add(s[i]);
                    break;
            }
        }
        FlushClaim();
        return new RemittanceAdvice(payer, payeeNpi, trace.Length == 0 ? null : trace, paidOn, total, method, claims);

        void FlushService()
        {
            if (service is null || claim is null) return;
            claim.Services.Add(new ServicePayment(service.Code, service.Modifiers, service.Charge, service.Paid, service.Units,
                service.ServiceDate, service.Allowed, service.Adjustments, service.Remarks));
            service = null;
        }

        void FlushClaim()
        {
            FlushService();
            if (claim is null) return;
            claims.Add(new ClaimPayment(claim.Number, claim.Status, claim.Charge, claim.Paid, claim.PatientResp, claim.PayerClaimId,
                claim.Adjustments, claim.Services, claim.Remarks));
            claim = null;
        }
    }

    // CAS carries up to six reason/amount/quantity triplets after the group code
    private static IEnumerable<Adjustment> ReadCas(Segment s)
    {
        for (var i = 2; i + 1 < s.Elements.Count && i <= 17; i += 3)
            if (s[i].Length > 0)
                yield return new Adjustment(s[1], s[i], X12Values.Amount(s[i + 1]));
    }

    public string Write(InterchangeOptions options, string payeeName)
    {
        var w = new X12Writer(options, "HP", "835", Version);
        var paid = PaymentDate ?? DateOnly.FromDateTime(options.CreatedAt.UtcDateTime);
        w.Add("BPR", TotalPaid == 0 ? "H" : "I", X12Writer.Amount(TotalPaid), "C", TotalPaid == 0 ? "NON" : PaymentMethod, "CCP", "01",
            "999999999", "DA", "000111222", "1999999999", "", "01", "999999999", "DA", "000333444", X12Writer.Date(paid));
        w.Add("TRN", "1", EftTraceNumber ?? "0", "1999999999");
        w.Add("DTM", "405", X12Writer.Date(paid));
        w.Add("N1", "PR", X12Writer.Upper(PayerName));
        w.Add("N1", "PE", X12Writer.Upper(payeeName), "XX", PayeeNpi);
        w.Add("LX", "1");
        foreach (var c in Claims)
        {
            w.Add("CLP", c.ClaimNumber, c.StatusCode, X12Writer.Amount(c.ChargeAmount), X12Writer.Amount(c.PaidAmount),
                X12Writer.Amount(c.PatientResponsibility), "12", c.PayerClaimId, "11", "1");
            WriteCas(w, c.ClaimAdjustments);
            foreach (var remark in c.RemarkCodes) w.Add("LQ", "HE", remark);
            foreach (var svc in c.Services)
            {
                w.Add("SVC", w.Composite(["HC", svc.ProcedureCode, .. svc.Modifiers]), X12Writer.Amount(svc.Charge),
                    X12Writer.Amount(svc.Paid), "", X12Writer.Amount(svc.Units));
                if (svc.ServiceDate is { } dos) w.Add("DTM", "472", X12Writer.Date(dos));
                WriteCas(w, svc.Adjustments);
                if (svc.Allowed is { } allowed) w.Add("AMT", "B6", X12Writer.Amount(allowed));
                foreach (var remark in svc.RemarkCodes) w.Add("LQ", "HE", remark);
            }
        }
        return w.Build();
    }

    private static void WriteCas(X12Writer w, IEnumerable<Adjustment> adjustments)
    {
        foreach (var group in adjustments.GroupBy(a => a.GroupCode))
        {
            var elements = new List<string?> { group.Key };
            foreach (var a in group.Take(6))
            {
                elements.Add(a.ReasonCode);
                elements.Add(X12Writer.Amount(a.Amount));
                elements.Add("");
            }
            w.Add("CAS", [.. elements]);
        }
    }

    private sealed class ClaimState(string number, string status, decimal charge, decimal paid, decimal patientResp, string? payerClaimId)
    {
        public string Number { get; } = number;
        public string Status { get; } = status;
        public decimal Charge { get; } = charge;
        public decimal Paid { get; } = paid;
        public decimal PatientResp { get; } = patientResp;
        public string? PayerClaimId { get; } = payerClaimId;
        public List<Adjustment> Adjustments { get; } = [];
        public List<ServicePayment> Services { get; } = [];
        public List<string> Remarks { get; } = [];
    }

    private sealed class ServiceState(string code, List<string> modifiers, decimal charge, decimal paid, decimal units)
    {
        public string Code { get; } = code;
        public List<string> Modifiers { get; } = modifiers;
        public decimal Charge { get; } = charge;
        public decimal Paid { get; } = paid;
        public decimal Units { get; } = units;
        public DateOnly? ServiceDate { get; set; }
        public decimal? Allowed { get; set; }
        public List<Adjustment> Adjustments { get; } = [];
        public List<string> Remarks { get; } = [];
    }
}

/// <summary>Sniffs which transaction an inbound file carries without fully parsing it.</summary>
public static class X12FileKind
{
    public static string Detect(string text)
    {
        var interchange = X12Reader.Parse(text);
        return interchange.TransactionType;
    }
}
