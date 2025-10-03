namespace ClaimBridge.PayerSim.Engine;

public sealed record SimMember(
    string MemberId,
    string PayerId,
    string FirstName,
    string LastName,
    DateOnly DateOfBirth,
    string Gender,
    string Plan,
    DateOnly CoverageStart,
    DateOnly? CoverageEnd,
    decimal Copay,
    decimal Deductible,
    decimal DeductibleMet);

public sealed record SimLine(int LineNumber, string ProcedureCode, IReadOnlyList<string> Modifiers, decimal Units, decimal Charge,
    DateOnly ServiceDate, string PlaceOfService);

/// <summary>A claim as the simulated payer sees it, whatever channel it came in on.</summary>
public sealed record SimClaim(
    string PayerId,
    string ClaimNumber,
    string FrequencyCode,
    string? OriginalReference,
    string MemberId,
    DateOnly MemberDob,
    string BillingNpi,
    string BillingName,
    string? PriorAuthNumber,
    IReadOnlyList<string> Diagnoses,
    IReadOnlyList<SimLine> Lines)
{
    public decimal TotalCharge => Lines.Sum(l => l.Charge);
}

public enum SimClaimState { Received, Rejected, Pended, InReview, Finalized }

public sealed class SimClaimRecord(SimClaim claim, string payerClaimId)
{
    public SimClaim Claim { get; } = claim;
    public string PayerClaimId { get; } = payerClaimId;
    public SimClaimState State { get; set; } = SimClaimState.Received;
    public string? Outcome { get; set; }
    public string? Note { get; set; }
    public decimal Paid { get; set; }
    public ClaimBridge.X12.Transactions.ClaimPayment? Payment { get; set; }
}

/// <summary>Knobs for exercising failure paths, set at runtime through POST /sim/scenarios.</summary>
public sealed class SimScenario
{
    public bool PayerCOutage { get; set; }
    public double PayerCFailureRate { get; set; } = 0.15;
    public double PayerCTimeoutRate { get; set; } = 0.05;
    public int AdjudicationDelaySeconds { get; set; } = 20;
    public int PayerBAckDelaySeconds { get; set; } = 5;
    public bool SilentPayerA { get; set; }
    public int PendNext { get; set; }
    public bool Reject999Next { get; set; }
    public double RandomDenialRate { get; set; } = 0.05;
}
