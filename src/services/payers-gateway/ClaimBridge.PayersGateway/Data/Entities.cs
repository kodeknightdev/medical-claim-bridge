namespace ClaimBridge.PayersGateway.Data;

public enum SubmissionStatus { Queued, Deferred, Sent, Accepted, Rejected, Failed }

public class Submission
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public Guid OrgId { get; set; }
    public string PayerId { get; set; } = "";
    public int Attempt { get; set; }
    public string Channel { get; set; } = "";
    public SubmissionStatus Status { get; set; }
    public string? PayerClaimId { get; set; }
    public string? FileName { get; set; }
    public string? InterchangeControlNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? AckAt { get; set; }
    public string? RejectReasons { get; set; }
    public int DeliveryAttempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public string? LastPayerStatus { get; set; }
    public DateTimeOffset? LastInquiryAt { get; set; }
}

/// <summary>Every acknowledgement, status response and file touching a claim, for the claim timeline.</summary>
public class PayerStatusEntry
{
    public long Id { get; set; }
    public Guid ClaimId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public string PayerId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Detail { get; set; }
    public DateTimeOffset At { get; set; }
}
