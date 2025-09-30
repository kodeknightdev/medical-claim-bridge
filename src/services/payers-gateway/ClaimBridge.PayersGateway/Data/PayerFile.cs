namespace ClaimBridge.PayersGateway.Data;

public enum FileDirection { Outbound, Inbound }

/// <summary>Every X12 file exchanged with a payer, kept for audit and for matching 999s back to their 837.</summary>
public class PayerFile
{
    public Guid Id { get; set; }
    public string PayerId { get; set; } = "";
    public FileDirection Direction { get; set; }
    public string Kind { get; set; } = "";
    public string FileName { get; set; } = "";
    public string BlobContainer { get; set; } = "";
    public string BlobName { get; set; } = "";
    public string? ControlNumber { get; set; }
    public int ClaimCount { get; set; }
    public string Status { get; set; } = "";
    public string? Error { get; set; }
    public DateTimeOffset At { get; set; }
}
