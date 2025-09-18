namespace ClaimBridge.Contracts.Events;

public sealed record AdjustmentSummary(string GroupCode, string ReasonCode, decimal Amount);

public sealed record PaymentPosted(Guid ClaimId, string ClaimNumber, Guid OrgId, string PayerId, Guid RemittanceId,
    decimal ChargeAmount, decimal PaidAmount, decimal AllowedAmount, decimal PatientResponsibility,
    IReadOnlyList<AdjustmentSummary> Adjustments, bool PartiallyPaid, string? EftTraceNumber, DateOnly? PaymentDate)
    : IIntegrationEvent
{
    public static string EventType => "remittance.payment-posted";
    public static int SchemaVersion => 1;
}

public sealed record ClaimDenied(Guid ClaimId, string ClaimNumber, Guid OrgId, string PayerId, Guid RemittanceId,
    decimal ChargeAmount, string CarcCode, string Category, IReadOnlyList<string> RarcCodes, DateOnly? PaymentDate)
    : IIntegrationEvent
{
    public static string EventType => "remittance.claim-denied";
    public static int SchemaVersion => 1;
}

public sealed record ReversalPosted(Guid ClaimId, string ClaimNumber, Guid OrgId, string PayerId, Guid RemittanceId,
    decimal ReversedAmount, string? EftTraceNumber) : IIntegrationEvent
{
    public static string EventType => "remittance.reversal-posted";
    public static int SchemaVersion => 1;
}
