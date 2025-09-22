namespace ClaimBridge.Claims.Data;

/// <summary>
/// Denormalised read model behind claim search and the portal list. Written in the same transaction as the
/// aggregate, never read back by the write side, and free of PHI apart from the member id's last four.
/// </summary>
public class ClaimSearchEntry
{
    public Guid ClaimId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public Guid OrgId { get; set; }
    public string PayerId { get; set; } = "";
    public string Status { get; set; } = "";
    public string FrequencyCode { get; set; } = "1";
    public int Version { get; set; }
    public string MemberIdLast4 { get; set; } = "";
    public string MemberIdHash { get; set; } = "";
    public string ProcedureCodes { get; set; } = "";
    public decimal TotalCharge { get; set; }
    public decimal? PaidAmount { get; set; }
    public decimal Balance { get; set; }
    public DateOnly FirstServiceDate { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset StatusChangedAt { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public decimal? DenialRisk { get; set; }
    public string? DenialCategory { get; set; }
    public string Source { get; set; } = "";
    public bool HasOpenEdits { get; set; }
}
