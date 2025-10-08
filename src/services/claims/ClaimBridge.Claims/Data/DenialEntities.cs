namespace ClaimBridge.Claims.Data;

public enum DenialStatus { Open, Corrected, Appealed, Resolved, WrittenOff }

/// <summary>Read model behind the denial worklist, grouped by reason category, payer and age.</summary>
public class DenialWorkItem
{
    public Guid ClaimId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public Guid OrgId { get; set; }
    public string PayerId { get; set; } = "";
    public string CarcCode { get; set; } = "";
    public string Category { get; set; } = "";
    public decimal DeniedAmount { get; set; }
    public DateTimeOffset DeniedAt { get; set; }
    public DateOnly AppealDeadline { get; set; }
    public DenialStatus Status { get; set; }
    public Guid? CorrectedByClaimId { get; set; }
    public Guid? AppealId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public enum AppealStatus { Draft, Submitted, Won, Lost, Withdrawn }

public class Appeal
{
    public Guid Id { get; set; }
    public Guid ClaimId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public Guid OrgId { get; set; }
    public string PayerId { get; set; } = "";
    public string CarcCode { get; set; } = "";
    public string Argument { get; set; } = "";
    public List<string> Enclosures { get; set; } = [];
    public AppealStatus Status { get; set; }
    public DateOnly Deadline { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public DateTimeOffset? OutcomeAt { get; set; }
    public string? OutcomeNote { get; set; }
    public string? LetterBlobName { get; set; }
}
