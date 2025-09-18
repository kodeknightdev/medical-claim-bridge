namespace ClaimBridge.Contracts.Events;

public sealed record ClaimSubmitted(Guid ClaimId, string ClaimNumber, string PayerId, Guid SubmissionId, int Attempt,
    string Channel, DateTimeOffset SubmittedAt, string? FileName) : IIntegrationEvent
{
    public static string EventType => "payers.claim-submitted";
    public static int SchemaVersion => 1;
}

public sealed record AckReceived(Guid ClaimId, string ClaimNumber, string PayerId, bool Accepted, string? PayerClaimId,
    IReadOnlyList<string> RejectionReasons, string AckType, DateTimeOffset ReceivedAt) : IIntegrationEvent
{
    public static string EventType => "payers.ack-received";
    public static int SchemaVersion => 1;
}

/// <summary>Status from a 277-style response or a payer callback: pended, in-process, or finalized with an outcome.</summary>
public sealed record PayerStatusUpdated(Guid ClaimId, string ClaimNumber, string PayerId, string Status, string? Outcome,
    string? StatusCode, string? Description, string Source, DateTimeOffset At) : IIntegrationEvent
{
    public static string EventType => "payers.status-updated";
    public static int SchemaVersion => 1;
}

public sealed record RemittanceReceived(Guid RemittanceFileId, string PayerId, string BlobContainer, string BlobName,
    string? EftTraceNumber, DateTimeOffset ReceivedAt) : IIntegrationEvent
{
    public static string EventType => "payers.remittance-received";
    public static int SchemaVersion => 1;
}
