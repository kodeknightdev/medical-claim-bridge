namespace ClaimBridge.Remittance.Data;

/// <summary>Claims the remittance service knows about, learned from claims.status-changed (choreography).</summary>
public class ExpectedClaim
{
    public Guid ClaimId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public Guid OrgId { get; set; }
    public string PayerId { get; set; } = "";
    public decimal TotalCharge { get; set; }
    public string LastStatus { get; set; } = "";
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateOnly? FirstServiceDate { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class RemittanceFile
{
    public Guid Id { get; set; }
    public string PayerId { get; set; } = "";
    public string BlobContainer { get; set; } = "";
    public string BlobName { get; set; } = "";
    public string? EftTraceNumber { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public decimal TotalPaid { get; set; }
    public string PaymentMethod { get; set; } = "";
    public int ClaimCount { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedCount { get; set; }
    public string Status { get; set; } = "";
    public string? Error { get; set; }
    public string? PdfBlobName { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}

public class Posting
{
    public Guid Id { get; set; }
    public Guid RemittanceFileId { get; set; }
    public Guid ClaimId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public Guid OrgId { get; set; }
    public string PayerId { get; set; } = "";
    public string Kind { get; set; } = "";
    public string ClpStatus { get; set; } = "";
    public string? PayerClaimId { get; set; }
    public decimal Charge { get; set; }
    public decimal Paid { get; set; }
    public decimal Allowed { get; set; }
    public decimal PatientResponsibility { get; set; }
    public decimal ContractualAdjustments { get; set; }
    public decimal OtherAdjustments { get; set; }
    public string? DenialCarc { get; set; }
    public string? DenialCategory { get; set; }
    public List<string> RemarkCodes { get; set; } = [];
    public string? EftTraceNumber { get; set; }
    public DateOnly? PaymentDate { get; set; }
    public DateTimeOffset PostedAt { get; set; }
    public List<PostingAdjustment> Adjustments { get; set; } = [];
}

public class PostingAdjustment
{
    public long Id { get; set; }
    public Guid PostingId { get; set; }
    public string GroupCode { get; set; } = "";
    public string ReasonCode { get; set; } = "";
    public decimal Amount { get; set; }
}

/// <summary>Ops reconciliation queue: remittance lines that could not be posted automatically.</summary>
public class UnmatchedLine
{
    public Guid Id { get; set; }
    public Guid RemittanceFileId { get; set; }
    public string PayerId { get; set; } = "";
    public string ClaimNumber { get; set; } = "";
    public string? PayerClaimId { get; set; }
    public decimal Charge { get; set; }
    public decimal Paid { get; set; }
    public string Reason { get; set; } = "";
    public string RawClpJson { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string? ResolvedBy { get; set; }
    public string? Resolution { get; set; }
}

public class ReasonCode
{
    public string Kind { get; set; } = "";
    public string Code { get; set; } = "";
    public string? Category { get; set; }
    public string Explanation { get; set; } = "";
}

public class AuditLogEntry
{
    public long Id { get; set; }
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Target { get; set; } = "";
    public string? Detail { get; set; }
}
