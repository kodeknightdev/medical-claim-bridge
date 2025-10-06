namespace ClaimBridge.Remittance.Data;

public enum BalanceStatus { Open, Paid, Denied, Closed }

/// <summary>Accounts-receivable position of one claim, opened when it is submitted and moved by every posting.</summary>
public class ClaimBalance
{
    public Guid ClaimId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public Guid OrgId { get; set; }
    public string PayerId { get; set; } = "";
    public decimal Charge { get; set; }
    public decimal Paid { get; set; }
    public decimal Adjusted { get; set; }
    public decimal PatientResponsibility { get; set; }
    public decimal PayerOutstanding { get; set; }
    public BalanceStatus Status { get; set; }
    public DateTimeOffset OpenedAt { get; set; }
    public DateOnly? FirstServiceDate { get; set; }
    public DateTimeOffset? LastPaymentAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public void Recalculate() => PayerOutstanding = Status == BalanceStatus.Closed ? 0 : Charge - Paid - Adjusted - PatientResponsibility;
}
