namespace ClaimBridge.Contracts.Events;

public sealed record BatchUploaded(Guid BatchId, Guid OrgId, string Format, string BlobContainer, string BlobName, string UploadedBy)
    : IIntegrationEvent
{
    public static string EventType => "claims.batch-uploaded";
    public static int SchemaVersion => 1;
}

public sealed record ClaimReceived(Guid ClaimId, string ClaimNumber, Guid OrgId, string PayerId, int Version, string Source)
    : IIntegrationEvent
{
    public static string EventType => "claims.claim-received";
    public static int SchemaVersion => 1;
}

public sealed record ScrubEditSummary(string RuleCode, string Severity, int? LineNumber);

public sealed record ClaimScrubbed(Guid ClaimId, string ClaimNumber, Guid OrgId, string PayerId, bool Passed,
    IReadOnlyList<ScrubEditSummary> Edits, decimal? DenialRisk, bool FirstAttempt) : IIntegrationEvent
{
    public static string EventType => "claims.claim-scrubbed";
    public static int SchemaVersion => 1;
}

/// <summary>Claim check: carries only ids; payers-gateway pulls the submission document over the internal API.</summary>
public sealed record ClaimReady(Guid ClaimId, string ClaimNumber, Guid OrgId, string PayerId, int Attempt) : IIntegrationEvent
{
    public static string EventType => "claims.claim-ready";
    public static int SchemaVersion => 1;
}

public sealed record ClaimStatusChanged(
    Guid ClaimId,
    string ClaimNumber,
    Guid OrgId,
    string PayerId,
    string FromStatus,
    string ToStatus,
    string Trigger,
    string? Reason,
    decimal TotalCharge,
    decimal? PaidAmount,
    decimal? PatientResponsibility,
    string? DenialCategory,
    string? CarcCode,
    DateTimeOffset? SubmittedAt,
    DateOnly? FirstServiceDate,
    bool IsCorrection) : IIntegrationEvent
{
    public static string EventType => "claims.status-changed";
    public static int SchemaVersion => 1;
}
