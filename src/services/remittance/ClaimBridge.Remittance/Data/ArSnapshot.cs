namespace ClaimBridge.Remittance.Data;

public class ArSnapshot
{
    public long Id { get; set; }
    public DateOnly SnapshotDate { get; set; }
    public Guid OrgId { get; set; }
    public string PayerId { get; set; } = "";
    public decimal Days0To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal Days90Plus { get; set; }
    public int OpenClaims { get; set; }
    public decimal TotalOutstanding { get; set; }
}
